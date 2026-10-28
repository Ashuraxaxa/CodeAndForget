using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mantenedores.Tests;

/// <summary>Observabilidad (P-053, D-071): exportación OTLP opcional y log de solicitudes lentas.</summary>
public sealed class TelemetryTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("telemetry-tests-").FullName;

    private WebApplicationFactory<Program> Factory(LogSink? sink, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            if (sink is not null) b.ConfigureLogging(l => l.AddProvider(sink));
        });

    [Fact]
    public async Task Exporta_trazas_por_otlp_http_a_la_ruta_de_cada_senal()
    {
        // Receptor OTLP falso: anota a qué rutas llegan los envíos.
        HttpListener listener = null!;
        var port = 0;
        for (var attempt = 0; listener is null || !listener.IsListening; attempt++)
        {
            port = Random.Shared.Next(20000, 40000);
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { listener.Start(); }
            catch (HttpListenerException) when (attempt < 10) { listener.Close(); } // puerto ocupado: otro
        }
        using var owned = listener;
        var paths = new ConcurrentBag<string>();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    paths.Add(ctx.Request.Url!.AbsolutePath);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                }
                catch (Exception) when (!listener.IsListening) { return; }
            }
        });

        // Exportar cada poco (OpenTelemetry lee estas variables de la configuración) y esperar a que lleguen.
        await using var app = Factory(null,
            ("Telemetry:OtlpEndpoint", $"http://127.0.0.1:{port}"), ("Telemetry:OtlpProtocol", "HttpProtobuf"),
            ("OTEL_BSP_SCHEDULE_DELAY", "100"), ("OTEL_METRIC_EXPORT_INTERVAL", "200"));
        var c = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/config")).StatusCode);
        for (var i = 0; i < 100 && !(paths.Contains("/v1/traces") && paths.Contains("/v1/metrics")); i++)
            await Task.Delay(100);
        Assert.Contains("/v1/traces", paths);
        Assert.Contains("/v1/metrics", paths);
    }

    [Fact]
    public async Task Sin_destino_no_registra_opentelemetry()
    {
        await using var app = Factory(null);
        _ = app.CreateClient();
        Assert.Null(app.Services.GetService<OpenTelemetry.Trace.TracerProvider>());
    }

    [Fact]
    public async Task Anota_las_solicitudes_lentas_sin_la_query()
    {
        var sink = new LogSink();
        await using var app = Factory(sink, ("Telemetry:SlowRequestMilliseconds", "1"));
        var c = app.CreateClient();
        await c.PostAsJsonAsync("/api/auth/login?secreto=1", new { username = "admin", password = "admin" }); // PBKDF2: más de 1 ms
        var line = Assert.Single(sink.Messages, m => m.Contains("Solicitud lenta"));
        Assert.Contains("POST /api/auth/login → 200", line);
        Assert.DoesNotContain("secreto", line);
    }

    public sealed class LogSink : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(LogSink sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) sink.Messages.Enqueue(formatter(state, exception));
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
