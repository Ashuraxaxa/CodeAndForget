using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Mantenedores.Api.Infrastructure;
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

    /// <summary>Idioma del correo: es (por defecto), en o ru (D-102). Lo leen otras personas, no quien hizo el cambio.</summary>
    public string Language { get; set; } = "es";
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
/// <param name="planAllows">¿El plan incluye los avisos? (P-116: desde Pro). Nulo = sí (pruebas).</param>
public sealed class SmtpChangeNotifier(NotificationOptions options, ILogger<SmtpChangeNotifier> logger, Func<bool>? planAllows = null)
    : BackgroundService, IChangeNotifier
{
    private readonly Channel<ChangeNotice> _queue = Channel.CreateBounded<ChangeNotice>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public bool Enabled => options.Enabled && options.Smtp.Host.Length > 0 && options.From.Length > 0 && (planAllows?.Invoke() ?? true);

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

    /// <summary>Asunto y texto del aviso, en el idioma de <see cref="NotificationOptions.Language"/>. Público para probarlo sin servidor de correo.</summary>
    public MailMessage Compose(ChangeNotice n)
    {
        var lang = ServerText.Language(options.Language);
        string L(string es, string en, string ru) => ServerText.Pick(lang, es, en, ru);
        var table = n.TableLabel is { Length: > 0 } label ? $"{label} ({n.Schema}.{n.Table})" : $"{n.Schema}.{n.Table}";
        var first = n.Entries[0];
        var who = first.User ?? L("alguien", "someone", "кто-то");
        var rows = n.Entries.Count;
        var verb = first.Operation switch
        {
            AuditOperation.Create => L("creó un registro en", "created a record in", "создал(а) запись в"),
            AuditOperation.Update => L("editó un registro de", "edited a record of", "изменил(а) запись в"),
            AuditOperation.Delete => L("eliminó un registro de", "deleted a record of", "удалил(а) запись из"),
            AuditOperation.Import => L($"importó {rows} filas en", $"imported {rows} rows into", $"импортировал(а) строк: {rows} в"),
            AuditOperation.Action => L("ejecutó una acción en", "ran an action on", "выполнил(а) действие в"),
            _ => L("cambió los vínculos de un registro de", "changed the links of a record of", "изменил(а) связи записи в")
        };

        var body = new StringBuilder();
        body.AppendLine($"{who} {verb} {table}, {L("conexión", "connection", "подключение")} {n.Connection}.");
        body.AppendLine($"{L("Fecha", "Date", "Дата")}: {first.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        foreach (var e in n.Entries.Take(Math.Max(1, options.MaxRowsPerMessage)))
        {
            body.AppendLine();
            if (e.RowKey is { } key) body.AppendLine($"{L("Registro", "Record", "Запись")}: {Uri.UnescapeDataString(key.Replace("&", ", "))}");
            if (options.AppUrl.Length > 0 && e.RowKey is { } k && e.Operation is not AuditOperation.Delete)
                body.AppendLine($"{options.AppUrl.TrimEnd('/')}/c/{Uri.EscapeDataString(n.Connection)}/t/{Uri.EscapeDataString(n.Schema)}/{Uri.EscapeDataString(n.Table)}/edit?{k}");
            if (n.Notify.IncludeValues) AppendValues(body, e, L("(vacío)", "(empty)", "(пусто)"));
        }
        if (rows > options.MaxRowsPerMessage)
        {
            var more = rows - options.MaxRowsPerMessage;
            body.AppendLine().AppendLine(L($"… y {more} filas más (ver la auditoría).", $"… and {more} more rows (see the audit log).",
                $"… и ещё строк: {more} (см. журнал аудита)."));
        }

        var message = new MailMessage
        {
            From = new MailAddress(options.From),
            Subject = $"{options.SubjectPrefix} {table}: {LowerFirst(ServerText.Operation(first.Operation, lang))} {L("por", "by", "—")} {who}".Trim(),
            Body = body.ToString(),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        foreach (var to in n.Notify.Emails) message.To.Add(to);
        return message;
    }

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static void AppendValues(StringBuilder body, AuditEntry e, string empty)
    {
        var before = Parse(e.OldValues, empty);
        var after = Parse(e.NewValues, empty);
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

    private static Dictionary<string, string> Parse(string? json, string empty)
    {
        if (json is null) return [];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.ValueKind switch
            {
                JsonValueKind.Null => empty,
                JsonValueKind.String => p.Value.GetString()!,
                _ => p.Value.GetRawText()
            });
    }
}
