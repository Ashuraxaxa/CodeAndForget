using System.Data.Common;
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

    /// <summary>Máximo de filas por exportación (P-017). Si el filtro trae más, se pide filtrar.</summary>
    public int ExportMaxRows { get; set; } = 100_000;

    /// <summary>Máximo de filas por importación.</summary>
    public int ImportMaxRows { get; set; } = 10_000;

    /// <summary>Tamaño máximo del archivo a importar, en MB.</summary>
    public int ImportMaxMegabytes { get; set; } = 20;
}

/// <summary>CRUD genérico sobre cualquier tabla descubierta.</summary>
public sealed partial class CrudService(SchemaService schemas, CrudOptions options)
{
    public async Task<PagedResult> ListAsync(string conn, string schema, string table, ListRequest request, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var (data, count) = new QueryBuilder(ctx.Provider.Dialect).BuildList(ctx.Schema, info, request);

        await using var db = await OpenAsync(ctx, ct);
        var total = await db.ExecuteScalarAsync<long>(Command(count, ct));
        var rows = await db.QueryAsync(Command(data, ct));

        var pageSize = Math.Clamp(request.PageSize, 1, ListRequest.MaxPageSize);
        return new PagedResult(rows.Cast<object>().Select(r => Normalize(info, Rename(ToRow(r), data.Renames))).ToList(),
            total, Math.Max(request.Page, 1), pageSize);
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
        var insert = new QueryBuilder(ctx.Provider.Dialect).BuildInsert(info, ParseValues(info, body));

        IReadOnlyDictionary<string, object?> keys;
        await using (var db = await OpenAsync(ctx, ct))
        {
            if (insert.ProvidedKeys is { } provided)
            {
                // El motor no devuelve la clave: es la que vino en el alta (P-020).
                await Translated(ctx, info, DbOperation.Insert, () => db.ExecuteAsync(Command(insert.Query, ct)));
                keys = provided;
            }
            else
            {
                var row = await Translated(ctx, info, DbOperation.Insert, () => db.QuerySingleAsync(Command(insert.Query, ct)));
                keys = new Dictionary<string, object?>(ToRow(row));
            }
        }
        return await GetByKeyAsync(ctx, info, keys, ct);
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
            if (await Translated(ctx, info, DbOperation.Update, () => db.ExecuteAsync(Command(query, ct))) == 0)
                throw new NotFoundException("El registro no existe.");
        }
        return await GetByKeyAsync(ctx, info, keyValues, ct);
    }

    public async Task DeleteAsync(string conn, string schema, string table, IReadOnlyDictionary<string, string> key, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildDelete(info, ParseKey(info, key));

        await using var db = await OpenAsync(ctx, ct);
        if (await Translated(ctx, info, DbOperation.Delete, () => db.ExecuteAsync(Command(query, ct))) == 0)
            throw new NotFoundException("El registro no existe.");
    }

    /// <summary>
    /// Ejecuta una escritura y, si la BD la rechaza por una restricción, la traduce a un mensaje
    /// entendible (P-003). Los errores que no se reconocen se propagan tal cual.
    /// </summary>
    private static async Task<T> Translated<T>(ConnectionContext ctx, TableInfo table, DbOperation op, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbException ex) when (DbErrorTranslator.Translate(ctx.Provider.ClassifyError(ex), table, op) is { } friendly)
        {
            throw friendly;
        }
    }

    // ---------- Relaciones N:M (P-015) ----------

    /// <summary>Valores vinculados a un registro por una relación N:M, con su etiqueta.</summary>
    public async Task<IReadOnlyList<LookupItem>> GetLinksAsync(
        string conn, string schema, string table, IReadOnlyDictionary<string, string> key, string linkName, CancellationToken ct)
    {
        var (ctx, _, link, bridge, target, localValue) = await ResolveLinkAsync(conn, schema, table, key, linkName, ct);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildLinkedValues(bridge, target, link, localValue);
        await using var db = await OpenAsync(ctx, ct);
        var rows = await db.QueryAsync(Command(query, ct));
        return rows.Cast<IDictionary<string, object?>>()
            .Select(r => new LookupItem(r["value"], r["label"]?.ToString()))
            .ToList();
    }

    /// <summary>
    /// Deja vinculados exactamente <paramref name="values"/>: inserta los nuevos y borra los quitados, en una transacción.
    /// Devuelve los valores vinculados al terminar.
    /// </summary>
    public async Task<IReadOnlyList<LookupItem>> SetLinksAsync(
        string conn, string schema, string table, IReadOnlyDictionary<string, string> key, string linkName,
        IReadOnlyList<JsonElement> values, CancellationToken ct)
    {
        var (ctx, _, link, bridge, target, localValue) = await ResolveLinkAsync(conn, schema, table, key, linkName, ct);
        if (!bridge.IsEditable) throw new OperationNotAllowedException($"{bridge.FullName} es de solo lectura.");
        var foreignCol = bridge.FindColumn(link.ForeignColumn)!;

        // Se comparan como texto invariante: así 5, "5" y 5L son el mismo valor.
        static string K(object? v) => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var wanted = new Dictionary<string, object?>();
        var errors = new Dictionary<string, string>();
        foreach (var v in values)
        {
            try
            {
                var converted = ValueConverter.FromJson(foreignCol, v)
                    ?? throw new ValidationException(link.Name, "no se aceptan valores vacíos");
                wanted.TryAdd(K(converted), converted);
            }
            catch (ValidationException ex) { errors[link.Name] = string.Join(" ", ex.Errors.Values); }
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var qb = new QueryBuilder(ctx.Provider.Dialect);
        await using (var db = await OpenAsync(ctx, ct))
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            var current = (await db.QueryAsync(Command(qb.BuildLinkedValues(bridge, target, link, localValue), ct, tx)))
                .Cast<IDictionary<string, object?>>().Select(r => r["value"]).ToList();
            var currentKeys = current.Select(K).ToHashSet();

            foreach (var old in current.Where(c => !wanted.ContainsKey(K(c))))
            {
                var delete = qb.BuildDelete(bridge, new Dictionary<string, object?> { [link.LocalColumn] = localValue, [link.ForeignColumn] = old });
                await Translated(ctx, bridge, DbOperation.Delete, () => db.ExecuteAsync(Command(delete, ct, tx)));
            }
            foreach (var (k, add) in wanted.Where(w => !currentKeys.Contains(w.Key)))
            {
                var insert = qb.BuildInsert(bridge, new Dictionary<string, object?> { [link.LocalColumn] = localValue, [link.ForeignColumn] = add });
                await Translated(ctx, bridge, DbOperation.Insert, () => db.ExecuteAsync(Command(insert.Query, ct, tx)));
            }
            await tx.CommitAsync(ct);
        }
        return await GetLinksAsync(conn, schema, table, key, linkName, ct);
    }

    private async Task<(ConnectionContext Ctx, TableInfo Owner, ManyToManyLink Link, TableInfo Bridge, TableInfo Target, object? LocalValue)>
        ResolveLinkAsync(string conn, string schema, string table, IReadOnlyDictionary<string, string> key, string linkName, CancellationToken ct)
    {
        var (ctx, owner) = await schemas.GetTableAsync(conn, schema, table, ct);
        var link = owner.ManyToMany.FirstOrDefault(l => string.Equals(l.Name, linkName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new NotFoundException($"{owner.FullName} no tiene la relación '{linkName}'.");
        var bridge = ctx.Schema.FindTable(link.BridgeSchema, link.BridgeTable)!;
        var target = ctx.Schema.FindTable(link.TargetSchema, link.TargetTable)!;
        // La columna local del puente apunta a la PK (de una columna) de esta tabla.
        var localValue = ParseKey(owner, key)[owner.PrimaryKey[0]];
        return (ctx, owner, link, bridge, target, localValue);
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
        return row is null
            ? throw new NotFoundException("El registro no existe.")
            : Normalize(info, Rename(ToRow(row), query.Renames));
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

            try { values[col.Name] = ValueConverter.ForWrite(col, element); }
            catch (ValidationException ex) { foreach (var e in ex.Errors) errors[e.Key] = e.Value; }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        return values;
    }

    private static async Task<DbConnection> OpenAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var db = ctx.Provider.CreateConnection(ctx.Definition.ConnectionString);
        await db.OpenAsync(ct);
        await ctx.Provider.InitializeSessionAsync(db, ct);
        return db;
    }

    /// <summary>Devuelve con su nombre final las columnas que el SELECT trae con alias corto (P-021).</summary>
    private static IDictionary<string, object?> Rename(IDictionary<string, object?> row, IReadOnlyDictionary<string, string>? renames)
    {
        if (renames is null) return row;
        foreach (var (alias, name) in renames)
        {
            if (!row.Remove(alias, out var value)) continue;
            row[name] = value;
        }
        return row;
    }

    private CommandDefinition Command(SqlQuery q, CancellationToken ct, DbTransaction? tx = null) =>
        new(q.Sql, new DynamicParameters(q.Parameters), tx, options.CommandTimeoutSeconds, cancellationToken: ct);

    /// <summary>
    /// Deja los valores en el mismo formato para todos los motores: cada driver devuelve fechas y horas
    /// con tipos distintos (Npgsql: DateOnly/TimeOnly; SqlClient: DateTime/TimeSpan).
    /// Resultado: Date → DateOnly ("AAAA-MM-DD"), Time → TimeOnly ("HH:MM:SS"), fecha con zona → UTC.
    /// </summary>
    private static IDictionary<string, object?> Normalize(TableInfo table, IDictionary<string, object?> row)
    {
        foreach (var col in table.Columns)
        {
            if (!row.TryGetValue(col.Name, out var value) || value is null) continue;
            row[col.Name] = (col.Type, value) switch
            {
                (LogicalType.Date, DateTime dt) => DateOnly.FromDateTime(dt),
                (LogicalType.Time, TimeSpan ts) => TimeOnly.FromTimeSpan(ts),
                // Columna con zona que el driver entrega sin zona (MySQL TIMESTAMP con sesión en UTC): es UTC.
                (LogicalType.DateTime, DateTime { Kind: DateTimeKind.Unspecified } dt) when col.HasTimeZone =>
                    DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                _ => value
            };
        }
        return row;
    }

    private static IDictionary<string, object?> ToRow(object row) =>
        ((IDictionary<string, object>)row).ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase);
}
