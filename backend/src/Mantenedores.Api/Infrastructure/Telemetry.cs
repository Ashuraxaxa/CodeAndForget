using System.Diagnostics;
using System.Security.Claims;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Observabilidad (P-053, D-071), sección <c>Telemetry</c>. Sin <c>OtlpEndpoint</c> no se exporta nada.</summary>
public sealed class TelemetryOptions
{
    public const string Section = "Telemetry";

    /// <summary>Destino OTLP (Collector, Jaeger, Grafana Tempo/Mimir, Azure Monitor, Datadog…). Vacío = apagado.</summary>
    public string OtlpEndpoint { get; set; } = "";

    /// <summary><c>Grpc</c> (puerto 4317) o <c>HttpProtobuf</c> (4318).</summary>
    public string OtlpProtocol { get; set; } = "Grpc";

    /// <summary>Encabezados para el destino, p. ej. <c>api-key=…</c> (se pueden dar por variable de entorno).</summary>
    public string OtlpHeaders { get; set; } = "";

    public string ServiceName { get; set; } = "mantenedores";

    /// <summary>Solicitudes más lentas que esto se anotan en el log como advertencia. 0 = no se anotan.</summary>
    public int SlowRequestMilliseconds { get; set; } = 2000;
}

public static class TelemetrySetup
{
    /// <summary>Fuentes de trazas y métricas de los drivers que las publican por sí mismos (incluye el uso del pool).</summary>
    private static readonly string[] DriverSources = ["Npgsql", "MySqlConnector"];

    public static void AddMantenedoresTelemetry(this IServiceCollection services, TelemetryOptions o)
    {
        services.AddSingleton(o);
        if (o.OtlpEndpoint.Length == 0) return;

        var http = string.Equals(o.OtlpProtocol, "HttpProtobuf", StringComparison.OrdinalIgnoreCase);
        // Con HTTP, una dirección dada desde código se usa tal cual: se le agrega la ruta de cada señal.
        Action<OtlpExporterOptions> Exporter(string signal) => e =>
        {
            e.Protocol = http ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
            e.Endpoint = new Uri(http ? o.OtlpEndpoint.TrimEnd('/') + "/v1/" + signal : o.OtlpEndpoint);
            if (o.OtlpHeaders.Length > 0) e.Headers = o.OtlpHeaders;
        };

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(o.ServiceName, serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString()))
            .WithTracing(t => t
                // Sin trazas de los health checks: el orquestador los consulta cada pocos segundos.
                .AddAspNetCoreInstrumentation(a => a.Filter = http => !http.Request.Path.StartsWithSegments("/api/health"))
                .AddSource(DriverSources)
                .AddOtlpExporter(Exporter("traces")))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(DriverSources)
                .AddMeter("Microsoft.AspNetCore.RateLimiting")
                .AddOtlpExporter(Exporter("metrics")));
    }

    /// <summary>
    /// Anota en el log las solicitudes lentas: método, ruta (sin la query, que puede traer valores), estado, usuario y
    /// duración. Funciona aunque no haya OpenTelemetry.
    /// </summary>
    public static void UseSlowRequestLog(this WebApplication app)
    {
        var o = app.Services.GetRequiredService<TelemetryOptions>();
        if (o.SlowRequestMilliseconds <= 0) return;
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mantenedores.Lentas");
        app.Use(async (http, next) =>
        {
            var start = Stopwatch.GetTimestamp();
            await next();
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms >= o.SlowRequestMilliseconds && http.Request.Path.StartsWithSegments("/api"))
                log.LogWarning("Solicitud lenta: {Method} {Path} → {Status} en {Milliseconds:F0} ms (usuario {User})",
                    http.Request.Method, http.Request.Path.Value, http.Response.StatusCode, ms,
                    http.User.FindFirstValue(ClaimTypes.Name) ?? "-");
        });
    }
}
