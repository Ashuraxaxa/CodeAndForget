using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Notifications;

namespace Mantenedores.Api.Notifications;

/// <summary>Avisos por correo (P-099, D-073), sección <c>Notifications</c>. Apagados por defecto.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>Enviar los avisos que configuren las tablas. Con <c>false</c> (por defecto), ninguna tabla avisa.</summary>
    public bool Enabled { get; set; }

    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>Remitente, p. ej. <c>Mantenedores &lt;no-responder@empresa.cl&gt;</c>.</summary>
    public string From { get; set; } = "";

    public string SubjectPrefix { get; set; } = "[Mantenedores]";

    /// <summary>Dirección pública de la app, para incluir un enlace al registro (opcional).</summary>
    public string AppUrl { get; set; } = "";

    /// <summary>Máximo de filas detalladas en el aviso de una importación.</summary>
    public int MaxRowsPerMessage { get; set; } = 20;
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS / SSL. Solo apagarlo con un relay interno de confianza.</summary>
    public bool EnableSsl { get; set; } = true;

    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Encola los avisos y los envía en segundo plano: guardar no espera al correo (D-073). Reintenta 3 veces y, si no
/// puede, lo deja en el log; el cambio ya está hecho y registrado en la auditoría.
/// </summary>
public sealed class SmtpChangeNotifier(NotificationOptions options, ILogger<SmtpChangeNotifier> logger) : BackgroundService, IChangeNotifier
{
    private readonly Channel<ChangeNotice> _queue = Channel.CreateBounded<ChangeNotice>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public bool Enabled => options.Enabled && options.Smtp.Host.Length > 0 && options.From.Length > 0;

    public void Enqueue(ChangeNotice notice)
    {
        if (!Enabled) return;
        if (!_queue.Writer.TryWrite(notice))
            logger.LogWarning("Cola de avisos llena: no se avisará el cambio en {Table}", $"{notice.Schema}.{notice.Table}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notice in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var message = Compose(notice);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await SendAsync(message, stoppingToken);
                    break;
                }
                catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "No se pudo enviar el aviso de {Table} a {Recipients}", $"{notice.Schema}.{notice.Table}",
                        string.Join(", ", notice.Notify.Emails));
                    break;
                }
            }
        }
    }

    private async Task SendAsync(MailMessage message, CancellationToken ct)
    {
        var s = options.Smtp;
        using var client = new SmtpClient(s.Host, s.Port) { EnableSsl = s.EnableSsl, Timeout = s.TimeoutSeconds * 1000 };
        if (s.User.Length > 0) client.Credentials = new NetworkCredential(s.User, s.Password);
        await client.SendMailAsync(message, ct);
    }

    /// <summary>Asunto y texto del aviso. Público para probarlo sin servidor de correo.</summary>
    public MailMessage Compose(ChangeNotice n)
    {
        var table = n.TableLabel is { Length: > 0 } label ? $"{label} ({n.Schema}.{n.Table})" : $"{n.Schema}.{n.Table}";
        var first = n.Entries[0];
        var who = first.User ?? "alguien";
        var verb = first.Operation switch
        {
            AuditOperation.Create => "creó un registro en",
            AuditOperation.Update => "editó un registro de",
            AuditOperation.Delete => "eliminó un registro de",
            AuditOperation.Import => $"importó {n.Entries.Count} filas en",
            _ => "cambió los vínculos de un registro de"
        };

        var body = new StringBuilder();
        body.AppendLine($"{who} {verb} {table}, conexión {n.Connection}.");
        body.AppendLine($"Fecha: {first.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        foreach (var e in n.Entries.Take(Math.Max(1, options.MaxRowsPerMessage)))
        {
            body.AppendLine();
            if (e.RowKey is { } key) body.AppendLine($"Registro: {Uri.UnescapeDataString(key.Replace("&", ", "))}");
            if (options.AppUrl.Length > 0 && e.RowKey is { } k && e.Operation is not AuditOperation.Delete)
                body.AppendLine($"{options.AppUrl.TrimEnd('/')}/c/{Uri.EscapeDataString(n.Connection)}/t/{Uri.EscapeDataString(n.Schema)}/{Uri.EscapeDataString(n.Table)}/edit?{k}");
            if (n.Notify.IncludeValues) AppendValues(body, e);
        }
        if (n.Entries.Count > options.MaxRowsPerMessage)
            body.AppendLine().AppendLine($"… y {n.Entries.Count - options.MaxRowsPerMessage} filas más (ver la auditoría).");

        var message = new MailMessage
        {
            From = new MailAddress(options.From),
            Subject = $"{options.SubjectPrefix} {table}: {OperationName(first.Operation)} por {who}".Trim(),
            Body = body.ToString(),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        foreach (var to in n.Notify.Emails) message.To.Add(to);
        return message;
    }

    private static void AppendValues(StringBuilder body, AuditEntry e)
    {
        var before = Parse(e.OldValues);
        var after = Parse(e.NewValues);
        foreach (var name in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(name, out var o);
            after.TryGetValue(name, out var v);
            body.AppendLine(e.Operation switch
            {
                AuditOperation.Update or AuditOperation.Links => $"  {name}: {o} → {v}",
                AuditOperation.Delete => $"  {name}: {o}",
                _ => $"  {name}: {v}"
            });
        }
    }

    private static Dictionary<string, string> Parse(string? json)
    {
        if (json is null) return [];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.ValueKind switch
            {
                JsonValueKind.Null => "(vacío)",
                JsonValueKind.String => p.Value.GetString()!,
                _ => p.Value.GetRawText()
            });
    }

    private static string OperationName(AuditOperation op) => op switch
    {
        AuditOperation.Create => "alta",
        AuditOperation.Update => "edición",
        AuditOperation.Delete => "baja",
        AuditOperation.Import => "importación",
        _ => "vínculos"
    };
}
