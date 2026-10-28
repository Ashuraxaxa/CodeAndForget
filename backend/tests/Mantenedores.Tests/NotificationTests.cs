using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mantenedores.Api.Notifications;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Notifications;
using Mantenedores.Core.Overrides;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mantenedores.Tests;

/// <summary>Avisos por correo de tablas críticas (P-099, D-073).</summary>
public sealed class NotificationTests
{
    private static readonly Mantenedores.Core.Schema.DatabaseSchema Raw = TestSchema.Build();

    [Fact]
    public void El_ajuste_se_valida_y_por_defecto_avisa_altas_ediciones_y_bajas()
    {
        var applied = OverrideApplier.Apply(Raw, new Dictionary<(string, string), TableOverride>
        {
            [("ventas", "producto")] = new() { Notify = new NotifyOverride { Emails = [" jefa@empresa.cl ", "JEFA@empresa.cl", "ti@empresa.cl"] } }
        });
        var notify = applied.FindTable("ventas", "producto")!.Notify!;
        Assert.Equal(["jefa@empresa.cl", "ti@empresa.cl"], notify.Emails);
        Assert.Equal([AuditOperation.Create, AuditOperation.Update, AuditOperation.Delete], notify.On.Order());
        Assert.True(notify.IncludeValues);
        Assert.Null(applied.FindTable("ventas", "categoria")!.Notify);

        var ex = Assert.Throws<ValidationException>(() => OverrideApplier.Validate(Raw, "ventas", "producto",
            new TableOverride { Notify = new NotifyOverride { Emails = ["no-es-correo"], On = ["borrar"] } }));
        Assert.Contains("notify.emails", ex.Errors.Keys);
        Assert.Contains("notify.on", ex.Errors.Keys);
    }

    private static NotificationOptions Options(int port = 25) => new()
    {
        Enabled = true, From = "Mantenedores <no-responder@empresa.cl>", AppUrl = "https://app.empresa.cl/mnt/",
        Smtp = new SmtpSettings { Host = "127.0.0.1", Port = port, EnableSsl = false, TimeoutSeconds = 5 }
    };

    private static ChangeNotice Notice(AuditOperation op, bool values = true, int rows = 1) => new("demo", "ventas", "producto", "Productos",
        new TableNotify { Emails = ["jefa@empresa.cl"], On = new HashSet<AuditOperation> { op }, IncludeValues = values },
        Enumerable.Range(1, rows).Select(i => new AuditEntry
        {
            At = new DateTimeOffset(2026, 10, 28, 12, 30, 0, TimeSpan.Zero), User = "ana", Connection = "demo", Schema = "ventas",
            Table = "producto", Operation = op, RowKey = $"id={i}",
            OldValues = op == AuditOperation.Update ? """{"precio":100}""" : null,
            NewValues = op == AuditOperation.Update ? """{"precio":120}""" : """{"codigo":"A-1","notas":null}"""
        }).ToList());

    [Fact]
    public void El_correo_dice_quien_que_y_cuando_con_los_valores_y_un_enlace()
    {
        var n = new SmtpChangeNotifier(Options(), NullLogger<SmtpChangeNotifier>.Instance);
        using var edit = n.Compose(Notice(AuditOperation.Update));
        Assert.Equal("[Mantenedores] Productos (ventas.producto): edición por ana", edit.Subject);
        Assert.Contains("ana editó un registro de Productos (ventas.producto), conexión demo.", edit.Body);
        Assert.Contains("2026-10-28 12:30:00 UTC", edit.Body);
        Assert.Contains("Registro: id=1", edit.Body);
        Assert.Contains("precio: 100 → 120", edit.Body);
        Assert.Contains("https://app.empresa.cl/mnt/c/demo/t/ventas/producto/edit?id=1", edit.Body);
        Assert.Equal("jefa@empresa.cl", Assert.Single(edit.To).Address);

        using var sinValores = n.Compose(Notice(AuditOperation.Update, values: false));
        Assert.DoesNotContain("precio", sinValores.Body);

        using var import = n.Compose(Notice(AuditOperation.Import, rows: 25));
        Assert.Contains("importó 25 filas", import.Body);
        Assert.Contains("… y 5 filas más", import.Body);
        Assert.Contains("notas: (vacío)", import.Body);
    }

    [Fact]
    public void Sin_smtp_o_apagado_no_hay_avisos()
    {
        Assert.False(new SmtpChangeNotifier(new NotificationOptions(), NullLogger<SmtpChangeNotifier>.Instance).Enabled);
        var noFrom = Options(); noFrom.From = "";
        Assert.False(new SmtpChangeNotifier(noFrom, NullLogger<SmtpChangeNotifier>.Instance).Enabled);
        Assert.True(new SmtpChangeNotifier(Options(), NullLogger<SmtpChangeNotifier>.Instance).Enabled);
        var off = Options(); off.Enabled = false;
        Assert.False(new SmtpChangeNotifier(off, NullLogger<SmtpChangeNotifier>.Instance).Enabled);
    }

    [Fact]
    public async Task Envia_por_smtp_en_segundo_plano()
    {
        using var smtp = new FakeSmtp();
        var notifier = new SmtpChangeNotifier(Options(smtp.Port), NullLogger<SmtpChangeNotifier>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await notifier.StartAsync(cts.Token);
        notifier.Enqueue(Notice(AuditOperation.Delete));

        var data = await smtp.Received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("RCPT TO:<jefa@empresa.cl>", smtp.Commands);
        Assert.Contains("eliminó un registro de", data);
        await notifier.StopAsync(default);
    }

    /// <summary>Servidor SMTP mínimo: acepta un mensaje y guarda los comandos y el contenido.</summary>
    private sealed class FakeSmtp : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port { get; }
        public ConcurrentQueue<string> Commands { get; } = new();
        public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeSmtp()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            await using var writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake");
            while (await reader.ReadLineAsync() is { } line)
            {
                Commands.Enqueue(line);
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) await writer.WriteLineAsync("250 fake");
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 go");
                    var body = new System.Text.StringBuilder();
                    while (await reader.ReadLineAsync() is { } l && l != ".") body.Append(l).Append("\r\n");
                    await writer.WriteLineAsync("250 queued");
                    Received.TrySetResult(DecodeBody(body.ToString()));
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
        }

        /// <summary>El cuerpo va en base64 o quoted-printable según el contenido: se decodifica para leerlo.</summary>
        private static string DecodeBody(string raw)
        {
            var parts = raw.Split("\r\n\r\n", 2);
            if (parts.Length < 2) return raw;
            var content = parts[1].Trim();
            if (parts[0].Contains("base64", StringComparison.OrdinalIgnoreCase))
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(content.Split("\r\n"))));
            return content.Replace("=\r\n", "");
        }

        public void Dispose() => _listener.Stop();
    }
}
