using System.Text.Json;
using Mantenedores.Core.Connections;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Health checks para orquestadores y balanceadores (P-008):
/// /api/health/live  → el proceso responde (sin dependencias).
/// /api/health/ready → además puede leer su almacén de conexiones (lista para atender).
/// /api/health       → igual que live (compatibilidad).
/// </summary>
public static class HealthChecks
{
    private const string Ready = "ready";

    public static void AddMantenedoresHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks().AddCheck<ConnectionStoreHealthCheck>("connection-store", tags: [Ready]);

    public static void MapMantenedoresHealthChecks(this WebApplication app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteAsync };
        app.MapHealthChecks("/api/health", live).AllowAnonymous();
        app.MapHealthChecks("/api/health/live", live).AllowAnonymous();
        app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(Ready),
            ResponseWriter = WriteAsync
        }).AllowAnonymous();
    }

    private static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status == HealthStatus.Healthy ? "ok" : "error",
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), error = e.Value.Description })
        }));
    }

    private sealed class ConnectionStoreHealthCheck(IConnectionStore store) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            try
            {
                await store.ListAsync(ct);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                // Sin detalles internos: solo el tipo de error, para no filtrar rutas ni secretos.
                return HealthCheckResult.Unhealthy($"No se pudo leer el almacén de conexiones ({ex.GetType().Name}).");
            }
        }
    }
}
