using Dapper;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;

namespace Mantenedores.Core.Services;

public sealed partial class CrudService
{
    /// <summary>
    /// Resumen de una tabla para una tarjeta de dashboard (P-127, D-111), con los permisos de quien mira: solo tablas
    /// y columnas que ve (las ocultas no se agrupan, calculan ni filtran) y las tablas del plan. En una tabla grande
    /// comparte la cola de consultas pesadas del listado (D-072).
    /// </summary>
    public async Task<AggregateResult> AggregateAsync(string conn, string schema, string table, AggregateRequest request, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildAggregate(ctx.LabelSchema, info, request);

        var estimate = await EstimateAsync(ctx, info, ct);
        var large = options.EstimateCountAbove > 0 && estimate >= options.EstimateCountAbove && options.MaxConcurrentHeavyQueries > 0;
        var gate = large ? _heavy.GetOrAdd(ctx.Definition.Id, _ => new SemaphoreSlim(options.MaxConcurrentHeavyQueries)) : null;
        if (gate is not null && !await gate.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, options.HeavyQueueSeconds)), ct))
            throw new BusyException($"Hay muchas consultas en curso en {info.FullName}, que es una tabla grande. Intente de nuevo en unos segundos.");
        try
        {
            await using var db = await OpenAsync(ctx, ct);
            var rows = (await Translated(ctx, info, DbOperation.Read, () => db.QueryAsync(Command(query, ct))))
                .Cast<object>().Select(ToRow).ToList();

            static object? Get(IDictionary<string, object?> r, string name) => r.TryGetValue(name, out var v) && v is not DBNull ? v : null;

            var limit = Math.Clamp(request.Limit, 1, AggregateRequest.MaxLimit);
            var truncated = request.GroupBy.Count > 0 && rows.Count > limit;
            var result = rows.Take(request.GroupBy.Count > 0 ? limit : 1).Select(r => new AggregateRow(
                Get(r, "g0"), Get(r, "g0l")?.ToString(), Get(r, "g1"), Get(r, "g1l")?.ToString(), Get(r, "v"))).ToList();
            return new AggregateResult(result, truncated);
        }
        finally { gate?.Release(); }
    }
}
