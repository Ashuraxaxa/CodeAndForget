using Mantenedores.Api.Licensing;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Services;
using Mantenedores.Core.Transfer;
using Microsoft.Net.Http.Headers;

namespace Mantenedores.Api.Endpoints;

/// <summary>Consulta de la auditoría de cambios (P-044, D-065). Exige el permiso ViewAudit (D-068).</summary>
public static class AuditEndpoints
{
    /// <summary>Tope de filas de una exportación de la auditoría (<c>Audit:ExportMaxRows</c>).</summary>
    public const int DefaultExportMax = 100_000;

    /// <summary>
    /// Lo que el plan deja ver de la auditoría (P-116): Gratis, los últimos 7 días; Pro, el último año. Lo anterior no se
    /// borra (sigue <c>Audit:RetentionDays</c>): vuelve a verse al pasar a un plan mayor.
    /// </summary>
    private static async Task<AuditQuery> WithinPlanAsync(AuditQuery query, LicenseService license, CancellationToken ct)
    {
        if ((await license.GetAsync(ct)).Limits.AuditDays is not { } days) return query;
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        return query.From is { } from && from > since ? query : query with { From = since };
    }

    public static void MapAudit(this IEndpointRouteBuilder app)
    {
        // ?conn=&schema=&table=&user=&op=&from=&to=&page=&pageSize= y la clave del registro como k.<columna>=valor.
        // Con perfil (P-095), solo las tablas y columnas que el usuario puede ver; las FK y N:M traen su texto (P-094).
        app.MapGet("/api/audit", async (HttpRequest req, AuditService audit, HttpAccessContext access, CrudService crud,
            SchemaService schemas, LicenseService license, CancellationToken ct) =>
        {
            var user = await access.CurrentAsync(ct);
            var query = AuditAccess.Restrict(await WithinPlanAsync(Parse(req.Query), license, ct), user, user.Unrestricted ? [] : await audit.Store.TablesAsync(ct));
            var page = await audit.Store.QueryAsync(query, ct);
            var entries = page.Entries.Select(e => AuditAccess.Mask(e, user)).Where(e => user.Unrestricted || !AuditAccess.IsEmptyForUser(e)).ToList();
            var labels = await AuditLabels.ForAsync(entries, crud, schemas, ct);
            return new AuditPageResponse(entries.Select(e => new AuditEntryResponse(e, labels.GetValueOrDefault(e.Id))).ToList(),
                page.Total, page.Page, page.PageSize);
        }).RequireAuthorization(Permissions.Policy(Permissions.ViewAudit));

        // Mismos filtros; ?format=csv|xlsx|json|xml. Todo lo que coincide, hasta Audit:ExportMaxRows filas.
        app.MapGet("/api/audit/export", async (HttpContext http, string? format, AuditService audit, HttpAccessContext access,
            CrudService crud, SchemaService schemas, IConfiguration config, LicenseService license, CancellationToken ct) =>
        {
            var fmt = TransferFormats.Parse(format ?? "csv");
            if (fmt != TransferFormat.Csv) await license.RequireAsync(Core.Licensing.Feature.AllFormats, ct);
            var user = await access.CurrentAsync(ct);
            var query = AuditAccess.Restrict(await WithinPlanAsync(Parse(http.Request.Query), license, ct), user, user.Unrestricted ? [] : await audit.Store.TablesAsync(ct))
                with { CountTotal = false, PageSize = AuditQuery.MaxPageSize };
            var max = config.GetValue("Audit:ExportMaxRows", DefaultExportMax);
            // Encabezados, operaciones y nombre del archivo en el idioma de la interfaz (D-102).
            var lang = Infrastructure.ServerText.Language(http.Request.Headers.AcceptLanguage.ToString());
            var names = AuditLabels.Headers(lang);

            http.Response.ContentType = TransferFormats.ContentType(fmt);
            http.Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = $"{Infrastructure.ServerText.Pick(lang, "auditoria", "audit", "audit")}-{DateTime.UtcNow:yyyyMMdd-HHmm}.{TransferFormats.Extension(fmt)}"
            }.ToString();
            await using var writer = TableWriter.Create(fmt, http.Response.Body, "auditoria");
            await writer.WriteHeaderAsync(names, ct);
            var written = 0;
            for (var n = 1; written < max; n++)
            {
                var page = await audit.Store.QueryAsync(query with { Page = n }, ct);
                if (page.Entries.Count == 0) break;
                var entries = page.Entries.Select(e => AuditAccess.Mask(e, user)).Where(e => user.Unrestricted || !AuditAccess.IsEmptyForUser(e)).ToList();
                var labels = await AuditLabels.ForAsync(entries, crud, schemas, ct);
                foreach (var e in entries.Take(max - written))
                {
                    var l = labels.GetValueOrDefault(e.Id);
                    await writer.WriteRowAsync([e.At.UtcDateTime, e.User, Infrastructure.ServerText.Operation(e.Operation, lang), e.Connection, e.Schema, e.Table,
                        e.RowKey is null ? null : AuditLabels.KeyText(e.RowKey), AuditLabels.ValuesText(e.OldValues, l), AuditLabels.ValuesText(e.NewValues, l)], ct);
                    written++;
                }
                if (page.Entries.Count < query.PageSize) break;
            }
            await writer.CompleteAsync(ct);
            return Results.Empty;
        }).RequireAuthorization(Permissions.Policy(Permissions.ViewAudit)).RequireRateLimiting(Infrastructure.RateLimiting.Heavy);
    }

    private static AuditQuery Parse(IQueryCollection q)
    {
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
        return new AuditQuery
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
        };
    }
}

/// <summary>Una entrada con el texto a mostrar de sus FK y vínculos N:M: <c>labels[columna][valor] = texto</c>.</summary>
public sealed record AuditEntryResponse(
    long Id, DateTimeOffset At, string? User, string Connection, string Schema, string Table, AuditOperation Operation,
    string? RowKey, string? OldValues, string? NewValues, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Labels)
{
    public AuditEntryResponse(AuditEntry e, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? labels)
        : this(e.Id, e.At, e.User, e.Connection, e.Schema, e.Table, e.Operation, e.RowKey, e.OldValues, e.NewValues, labels) { }
}

public sealed record AuditPageResponse(IReadOnlyList<AuditEntryResponse> Entries, long Total, int Page, int PageSize);

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
