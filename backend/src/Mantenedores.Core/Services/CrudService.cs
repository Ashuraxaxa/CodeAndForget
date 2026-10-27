using System.Text.Json;
using Dapper;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

public sealed class CrudOptions
{
    /// <summary>Tiempo máximo por consulta, en segundos.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>CRUD genérico sobre cualquier tabla descubierta.</summary>
public sealed class CrudService(SchemaService schemas, CrudOptions options)
{
    public async Task<PagedResult> ListAsync(string conn, string schema, string table, ListRequest request, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var (data, count) = new QueryBuilder(ctx.Provider.Dialect).BuildList(ctx.Schema, info, request);

        await using var db = await OpenAsync(ctx, ct);
        var total = await db.ExecuteScalarAsync<long>(Command(count, ct));
        var rows = await db.QueryAsync(Command(data, ct));

        var pageSize = Math.Clamp(request.PageSize, 1, ListRequest.MaxPageSize);
        return new PagedResult(rows.Select(ToRow).ToList(), total, Math.Max(request.Page, 1), pageSize);
    }

    public async Task<IDictionary<string, object?>> GetAsync(
        string conn, string schema, string table, IReadOnlyDictionary<string, string> key, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        return await GetByKeyAsync(ctx, info, ParseKey(info, key), ct);
    }

    public async Task<IDictionary<string, object?>> CreateAsync(
        string conn, string schema, string table, IReadOnlyDictionary<string, JsonElement> body, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildInsert(info, ParseValues(info, body));

        IDictionary<string, object?> keys;
        await using (var db = await OpenAsync(ctx, ct))
        {
            var row = await db.QuerySingleAsync(Command(query, ct));
            keys = ToRow(row);
        }
        return await GetByKeyAsync(ctx, info, new Dictionary<string, object?>(keys), ct);
    }

    public async Task<IDictionary<string, object?>> UpdateAsync(
        string conn, string schema, string table,
        IReadOnlyDictionary<string, string> key, IReadOnlyDictionary<string, JsonElement> body, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var keyValues = ParseKey(info, key);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildUpdate(info, keyValues, ParseValues(info, body));

        await using (var db = await OpenAsync(ctx, ct))
        {
            if (await db.ExecuteAsync(Command(query, ct)) == 0)
                throw new NotFoundException("El registro no existe.");
        }
        return await GetByKeyAsync(ctx, info, keyValues, ct);
    }

    public async Task DeleteAsync(string conn, string schema, string table, IReadOnlyDictionary<string, string> key, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildDelete(info, ParseKey(info, key));

        await using var db = await OpenAsync(ctx, ct);
        if (await db.ExecuteAsync(Command(query, ct)) == 0)
            throw new NotFoundException("El registro no existe.");
    }

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(
        string conn, string schema, string table, string? search, int take, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildLookup(info, search, take);

        await using var db = await OpenAsync(ctx, ct);
        var rows = await db.QueryAsync(Command(query, ct));
        return rows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return new LookupItem(d["value"], d["label"]?.ToString());
        }).ToList();
    }

    private async Task<IDictionary<string, object?>> GetByKeyAsync(
        ConnectionContext ctx, TableInfo info, IReadOnlyDictionary<string, object?> key, CancellationToken ct)
    {
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildGetByKey(ctx.Schema, info, key);
        await using var db = await OpenAsync(ctx, ct);
        var row = await db.QuerySingleOrDefaultAsync(Command(query, ct));
        return row is null ? throw new NotFoundException("El registro no existe.") : ToRow(row);
    }

    /// <summary>Convierte los valores de la clave (vienen como texto en la URL) a su tipo.</summary>
    private static Dictionary<string, object?> ParseKey(TableInfo table, IReadOnlyDictionary<string, string> raw)
    {
        if (!table.HasPrimaryKey)
            throw new OperationNotAllowedException($"{table.FullName} no tiene clave primaria.");

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in table.PrimaryKey)
        {
            var entry = raw.FirstOrDefault(e => string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase));
            if (entry.Key is null) throw new ValidationException(k, "falta el valor de la clave");
            result[k] = ValueConverter.FromString(table.FindColumn(k)!, entry.Value);
        }
        return result;
    }

    /// <summary>Valida y convierte el cuerpo JSON; junta todos los errores para mostrarlos de una vez.</summary>
    private static Dictionary<string, object?> ParseValues(TableInfo table, IReadOnlyDictionary<string, JsonElement> body)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>();

        foreach (var (name, element) in body)
        {
            if (name.EndsWith(QueryBuilder.LabelSuffix, StringComparison.Ordinal)) continue;

            var col = table.FindColumn(name);
            if (col is null) { errors[name] = "la columna no existe"; continue; }
            if (col.IsReadOnly) continue;

            try { values[col.Name] = ValueConverter.FromJson(col, element); }
            catch (ValidationException ex) { foreach (var e in ex.Errors) errors[e.Key] = e.Value; }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        return values;
    }

    private static async Task<System.Data.Common.DbConnection> OpenAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var db = ctx.Provider.CreateConnection(ctx.Definition.ConnectionString);
        await db.OpenAsync(ct);
        return db;
    }

    private CommandDefinition Command(SqlQuery q, CancellationToken ct) =>
        new(q.Sql, new DynamicParameters(q.Parameters), commandTimeout: options.CommandTimeoutSeconds, cancellationToken: ct);

    private static IDictionary<string, object?> ToRow(dynamic row) =>
        ((IDictionary<string, object>)row).ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase);
}
