using Mantenedores.Api.Auth;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Errors;

namespace Mantenedores.Api.Endpoints;

/// <summary>Consulta de la auditoría de cambios (P-044, D-065). Exige el permiso ViewAudit (D-068).</summary>
public static class AuditEndpoints
{
    public static void MapAudit(this IEndpointRouteBuilder app)
    {
        // ?conn=&schema=&table=&user=&op=&from=&to=&page=&pageSize= y la clave del registro como k.<columna>=valor.
        app.MapGet("/api/audit", (HttpRequest req, AuditService audit, CancellationToken ct) =>
        {
            var q = req.Query;
            string? S(string name) => q.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString().Trim() : null;
            AuditOperation? op = S("op") is { } o
                ? Enum.TryParse<AuditOperation>(o, true, out var parsed) ? parsed : throw new ValidationException("op", "operación desconocida")
                : null;
            DateTimeOffset? Date(string name) => S(name) is { } d
                ? DateTimeOffset.TryParse(d, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var dt) ? dt : throw new ValidationException(name, "fecha inválida")
                : null;
            var key = q.Where(kv => kv.Key.StartsWith("k.", StringComparison.Ordinal) && kv.Key.Length > 2)
                .Select(kv => new KeyValuePair<string, string?>(kv.Key[2..], kv.Value.ToString())).ToList();

            return audit.Store.QueryAsync(new AuditQuery
            {
                Connection = S("conn")?.ToLowerInvariant(),
                Schema = S("schema"),
                Table = S("table"),
                User = S("user"),
                Operation = op,
                From = Date("from"),
                To = Date("to"),
                RowKey = key.Count > 0 ? AuditValues.Key(key) : null,
                Page = int.TryParse(S("page"), out var page) ? page : 1,
                PageSize = int.TryParse(S("pageSize"), out var size) ? size : 50
            }, ct);
        }).RequireAuthorization(Permissions.Policy(Permissions.ViewAudit));
    }
}

/// <summary>Borra la auditoría vencida (<c>Audit:RetentionDays</c>) al arrancar y luego cada <c>Audit:PurgeIntervalHours</c>.</summary>
public sealed class AuditPurgeService(AuditService audit, AuditOptions options, ILogger<AuditPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RetentionDays <= 0) return;
        var every = TimeSpan.FromHours(Math.Max(1, options.PurgeIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await audit.PurgeExpiredAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Auditoría: {Count} entradas de más de {Days} días borradas", removed, options.RetentionDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo limpiar la auditoría vencida; se reintenta en el próximo ciclo");
            }
            try { await Task.Delay(every, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
