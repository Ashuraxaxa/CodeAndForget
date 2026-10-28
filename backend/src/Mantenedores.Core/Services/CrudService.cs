using System.Data.Common;
using System.Text.Json;
using Dapper;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Concurrency;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Notifications;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;

namespace Mantenedores.Core.Services;

public sealed class CrudOptions
{
    /// <summary>Tiempo máximo por consulta, en segundos.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Máximo de filas por exportación (P-017). Si el filtro trae más, se pide filtrar.</summary>
    public int ExportMaxRows { get; set; } = 100_000;

    /// <summary>Máximo de filas por importación.</summary>
    public int ImportMaxRows { get; set; } = 10_000;

    /// <summary>Tamaño máximo de un archivo guardado en una columna binaria, en MB (P-018).</summary>
    public int FileMaxMegabytes { get; set; } = 10;

    /// <summary>
    /// Sin filtros, si la tabla tiene al menos estas filas según las estadísticas del motor, el total del listado es esa
    /// estimación en vez de un COUNT(*) (D-072). 0 = contar siempre.
    /// </summary>
    public long EstimateCountAbove { get; set; } = 500_000;

    /// <summary>
    /// En esas mismas tablas grandes, con filtros o búsqueda se cuenta solo hasta este número ("más de 1.000"): contar
    /// todas las coincidencias recorrería la tabla entera en cada página (D-072). 0 = contar todo.
    /// </summary>
    public long FilteredCountCap { get; set; } = 1_000;

    /// <summary>
    /// En tablas grandes, cuántas consultas pesadas (búsqueda, filtros u orden por otra columna) corren a la vez por
    /// conexión; las demás esperan su turno sin tomar una conexión del pool, así abrir o paginar no se traba (D-072).
    /// 0 = sin tope.
    /// </summary>
    public int MaxConcurrentHeavyQueries { get; set; } = 4;

    /// <summary>Segundos que una consulta pesada espera su turno antes de responder "intente de nuevo".</summary>
    public int HeavyQueueSeconds { get; set; } = 30;

    /// <summary>Tamaño máximo del archivo a importar, en MB.</summary>
    public int ImportMaxMegabytes { get; set; } = 20;
}

/// <summary>CRUD genérico sobre cualquier tabla descubierta.</summary>
public sealed partial class CrudService(SchemaService schemas, CrudOptions options, AuditService? audit = null,
    IChangeNotifier? notifier = null)
{
    /// <summary>¿Hay que leer el registro antes de cambiarlo? Para la auditoría o para avisar por correo (D-073).</summary>
    private bool TracksChanges(TableInfo info) =>
        audit is { Enabled: true } || (info.Notify is not null && notifier is { Enabled: true });

    /// <summary>Registra un cambio ya confirmado (D-065). Sin auditoría configurada, no hace nada.</summary>
    private async Task AuditAsync(ConnectionContext ctx, TableInfo info, AuditOperation op,
        IEnumerable<(string? Key, string? Old, string? New)> changes, CancellationToken ct)
    {
        var notify = info.Notify is { } n && n.On.Contains(op) && notifier is { Enabled: true } ? n : null;
        if (audit is not { Enabled: true } && notify is null) return;
        var user = (await schemas.AccessAsync(ct)).Username;
        var now = DateTimeOffset.UtcNow;
        var entries = changes.Select(c => new AuditEntry
        {
            At = now, User = user, Connection = ctx.Definition.Id, Schema = info.Schema, Table = info.Name,
            Operation = op, RowKey = c.Key, OldValues = c.Old, NewValues = c.New
        }).ToList();
        if (audit is { Enabled: true }) await audit.RecordAsync(entries, ct);
        // Aviso por correo de una tabla crítica (D-073): se encola, no demora la respuesta.
        if (notify is not null && entries.Count > 0)
            notifier!.Enqueue(new ChangeNotice(ctx.Definition.Id, info.Schema, info.Name, info.Label, notify, entries));
    }

    private Task AuditAsync(ConnectionContext ctx, TableInfo info, AuditOperation op, string? key, string? old, string? @new, CancellationToken ct) =>
        AuditAsync(ctx, info, op, [(key, old, @new)], ct);

    public async Task<PagedResult> ListAsync(string conn, string schema, string table, ListRequest request, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var qb = new QueryBuilder(ctx.Provider.Dialect);
        var (data, count) = qb.BuildList(ctx.Schema, info, request);

        var estimate = await EstimateAsync(ctx, info, ct);
        var large = options.EstimateCountAbove > 0 && estimate >= options.EstimateCountAbove;
        var heavy = large && options.MaxConcurrentHeavyQueries > 0 && (!QueryBuilder.IsUnfiltered(request) || request.Sort.Count > 0);
        var gate = heavy ? _heavy.GetOrAdd(ctx.Definition.Id, _ => new SemaphoreSlim(options.MaxConcurrentHeavyQueries)) : null;
        if (gate is not null && !await gate.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, options.HeavyQueueSeconds)), ct))
            throw new BusyException($"Hay muchas búsquedas en curso en {info.FullName}, que es una tabla grande. Intente de nuevo en unos segundos o afine los filtros.");
        try
        {
            await using var db = await OpenAsync(ctx, ct);
            var rows = (await Translated(ctx, info, DbOperation.Read, () => db.QueryAsync(Command(data, ct)))).Cast<object>()
                .Select(r => RowVersion.Stamp(info, Normalize(info, Rename(ToRow(r), data.Renames)))).ToList();

            var pageSize = Math.Clamp(request.PageSize, 1, ListRequest.MaxPageSize);
            var page = Math.Max(request.Page, 1);
            // Si la página vino incompleta, el total ya se sabe (lo saltado + lo que llegó): no hace falta contar.
            // En una tabla grande, una búsqueda con pocas coincidencias se ahorra así un segundo recorrido (D-072).
            if (rows.Count < pageSize && (rows.Count > 0 || page == 1))
                return new PagedResult(rows, (long)(page - 1) * pageSize + rows.Count, page, pageSize);

            if (!large) return new PagedResult(rows, await db.ExecuteScalarAsync<long>(Command(count, ct)), page, pageSize);
            if (QueryBuilder.IsUnfiltered(request)) return new PagedResult(rows, estimate!.Value, page, pageSize, TotalIsEstimate: true);
            if (options.FilteredCountCap <= 0)
                return new PagedResult(rows, await db.ExecuteScalarAsync<long>(Command(count, ct)), page, pageSize);
            var found = await db.ExecuteScalarAsync<long>(Command(qb.BuildCappedCount(ctx.Schema, info, request, options.FilteredCountCap), ct));
            return found > options.FilteredCountCap
                ? new PagedResult(rows, options.FilteredCountCap, page, pageSize, TotalIsCapped: true)
                : new PagedResult(rows, found, page, pageSize);
        }
        finally { gate?.Release(); }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _heavy = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string, string), (long? Rows, DateTimeOffset At)> _estimates = new();
    private static readonly TimeSpan EstimateTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Filas estimadas de la tabla (D-072), guardadas unos minutos: decide si la tabla es "grande" antes de tomar una
    /// conexión para la consulta. Null si el motor no estima, es una vista o la tabla nunca se analizó.
    /// </summary>
    private async Task<long?> EstimateAsync(ConnectionContext ctx, TableInfo info, CancellationToken ct)
    {
        if (options.EstimateCountAbove <= 0) return null;
        var key = (ctx.Definition.Id, info.Schema, info.Name);
        if (_estimates.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < EstimateTtl) return cached.Rows;
        long? rows = null;
        if (new QueryBuilder(ctx.Provider.Dialect).BuildEstimatedCount(info) is { } q)
        {
            await using var db = await OpenAsync(ctx, ct);
            rows = await db.ExecuteScalarAsync<long?>(Command(q, ct));
        }
        _estimates[key] = (rows, DateTimeOffset.UtcNow);
        return rows;
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
        Require(info, DataOperation.Create, "crear registros en");
        var insert = new QueryBuilder(ctx.Provider.Dialect).BuildInsert(info, ParseValues(info, body, MaxFileBytes));

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
        var created = await GetByKeyAsync(ctx, info, keys, ct);
        var createdRow = created.AsReadOnly();
        await AuditAsync(ctx, info, AuditOperation.Create, AuditValues.Key(info, createdRow), null,
            AuditValues.Serialize(AuditValues.Columns(info, createdRow)), ct);
        return created;
    }

    public async Task<IDictionary<string, object?>> UpdateAsync(
        string conn, string schema, string table,
        IReadOnlyDictionary<string, string> key, IReadOnlyDictionary<string, JsonElement> body, CancellationToken ct,
        string? expectedVersion = null)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        Require(info, DataOperation.Update, "editar registros de");
        var keyValues = ParseKey(info, key);
        var values = ParseValues(info, body, MaxFileBytes);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildUpdate(info, keyValues, values);
        var before = await WriteAsync(ctx, info, keyValues, expectedVersion, TracksChanges(info), async (db, tx) =>
        {
            await CheckNoCycleAsync(ctx, info, db, keyValues[info.PrimaryKey[0]], values, ct, tx);
            if (await Translated(ctx, info, DbOperation.Update, () => db.ExecuteAsync(Command(query, ct, tx))) == 0)
                throw new NotFoundException("El registro no existe.");
        }, ct);
        var after = await GetByKeyAsync(ctx, info, keyValues, ct);
        if (before is not null && AuditValues.Diff(info, before.AsReadOnly(), after.AsReadOnly()) is var (oldV, newV))
            await AuditAsync(ctx, info, AuditOperation.Update, AuditValues.Key(info, after.AsReadOnly()), oldV, newV, ct);
        return after;
    }

    public async Task DeleteAsync(string conn, string schema, string table, IReadOnlyDictionary<string, string> key, CancellationToken ct,
        string? expectedVersion = null)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        Require(info, DataOperation.Delete, "eliminar registros de");
        var keyValues = ParseKey(info, key);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildDelete(info, keyValues);
        var before = await WriteAsync(ctx, info, keyValues, expectedVersion, TracksChanges(info), async (db, tx) =>
        {
            if (await Translated(ctx, info, DbOperation.Delete, () => db.ExecuteAsync(Command(query, ct, tx))) == 0)
                throw new NotFoundException("El registro no existe.");
        }, ct);

        if (before is not null)
            await AuditAsync(ctx, info, AuditOperation.Delete, AuditValues.Key(info, before.AsReadOnly()),
                AuditValues.Serialize(AuditValues.Columns(info, before.AsReadOnly())), null, ct);
    }

    /// <summary>
    /// Ejecuta una escritura y, si la BD la rechaza por una restricción, la traduce a un mensaje
    /// entendible (P-003). Los errores que no se reconocen se propagan tal cual.
    /// </summary>
    private async Task<T> Translated<T>(ConnectionContext ctx, TableInfo table, DbOperation op, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbException ex) when (DbErrorTranslator.Translate(ctx.Provider.ClassifyError(ex), table, op) is { } friendly)
        {
            // Una tabla o columna que ya no existe (P-086): se descarta el esquema en memoria, aquí y en las demás
            // instancias, para que el próximo intento use el actual y aparezca el aviso de cambios.
            if (friendly is SchemaChangedException)
                await schemas.SchemaChangedAsync(ctx.Definition.Id, CancellationToken.None);
            throw friendly;
        }
    }

    /// <summary>
    /// Concurrencia optimista (D-069): si el cliente dice qué versión editó y la fila ya no es esa, no se pisa.
    /// La comparación y la escritura no van bajo un bloqueo: la ventana es de milisegundos, frente a los minutos que
    /// un formulario puede estar abierto.
    /// </summary>
    private static void CheckVersion(IDictionary<string, object?>? current, string? expected)
    {
        if (expected is null || current is null) return;
        if (!string.Equals(current.TryGetValue(RowVersion.Field, out var v) ? v as string : null, expected, StringComparison.Ordinal))
            throw new ConcurrencyException(current);
    }

    /// <summary>Corta la operación si el perfil del usuario no la permite en la tabla (D-063).</summary>
    private static void Require(TableInfo table, DataOperation op, string verb)
    {
        if (!table.Access.HasFlag(op))
            throw new ForbiddenException($"No tiene permiso para {verb} {table.FullName}.");
    }

    // ---------- Archivos (P-018) ----------

    /// <summary>Archivo de una columna binaria: contenido, nombre sugerido y tipo (deducido de sus primeros bytes).</summary>
    public sealed record FileContent(byte[] Content, string FileName, string ContentType);

    public async Task<FileContent?> GetFileAsync(
        string conn, string schema, string table, IReadOnlyDictionary<string, string> key, string column, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var col = info.FindColumn(column) ?? throw new NotFoundException($"La columna '{column}' no existe.");
        var keyValues = ParseKey(info, key);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildGetFile(info, keyValues, col);
        await using var db = await OpenAsync(ctx, ct);
        if (await db.ExecuteScalarAsync<object?>(Command(query, ct)) is not byte[] bytes) return null;

        var (ext, type) = Sniff(bytes);
        var id = string.Join("-", info.PrimaryKey.Select(k => Convert.ToString(keyValues[k], System.Globalization.CultureInfo.InvariantCulture)));
        return new FileContent(bytes, $"{info.Name}-{col.Name}-{id}{ext}", type);
    }

    /// <summary>Tipo por la firma de los primeros bytes; si no se reconoce, binario genérico.</summary>
    private static (string Ext, string ContentType) Sniff(byte[] b)
    {
        bool Starts(params byte[] sig) => b.Length >= sig.Length && b.AsSpan(0, sig.Length).SequenceEqual(sig);
        if (Starts(0x89, 0x50, 0x4E, 0x47)) return (".png", "image/png");
        if (Starts(0xFF, 0xD8, 0xFF)) return (".jpg", "image/jpeg");
        if (Starts(0x47, 0x49, 0x46, 0x38)) return (".gif", "image/gif");
        if (Starts(0x25, 0x50, 0x44, 0x46)) return (".pdf", "application/pdf");
        if (Starts(0x50, 0x4B, 0x03, 0x04)) return (".zip", "application/zip");
        return (".bin", "application/octet-stream");
    }

    // ---------- Árboles (P-016) ----------

    /// <summary>Un nodo del árbol: su clave, su texto y si tiene hijos.</summary>
    public sealed record TreeNode(object? Value, string? Label, bool HasChildren);

    /// <summary>Un nivel del árbol: los hijos de <paramref name="parent"/>, o las raíces si es null.</summary>
    public async Task<IReadOnlyList<TreeNode>> TreeAsync(string conn, string schema, string table, string? parent, int take, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var fk = info.SelfReference ?? throw new OperationNotAllowedException($"{info.FullName} no tiene una relación consigo misma.");
        var parentValue = string.IsNullOrEmpty(parent) ? null : ValueConverter.FromString(info.FindColumn(fk.Columns[0])!, parent);
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildTreeLevel(info, parentValue, take);
        await using var db = await OpenAsync(ctx, ct);
        var rows = await db.QueryAsync(Command(query, ct));
        return rows.Cast<IDictionary<string, object?>>()
            .Select(r => new TreeNode(r["value"], r["label"]?.ToString(), Convert.ToInt32(r["children"]) == 1))
            .ToList();
    }

    /// <summary>
    /// Un registro no puede quedar como su propio ancestro: se sube desde el nuevo padre hacia la raíz y,
    /// si se llega al registro, se rechaza. Se usa SQL simple (sin CTE recursivo) para que sea igual en todos los motores.
    /// </summary>
    private async Task CheckNoCycleAsync(ConnectionContext ctx, TableInfo table, DbConnection db, object? self,
        IReadOnlyDictionary<string, object?> values, CancellationToken ct, DbTransaction? tx = null)
    {
        if (table.SelfReference is not { } fk) return;
        var parentName = fk.Columns[0];
        var entry = values.FirstOrDefault(v => string.Equals(v.Key, parentName, StringComparison.OrdinalIgnoreCase));
        if (entry.Key is null || entry.Value is null || self is null) return;

        static string K(object? v) => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var qb = new QueryBuilder(ctx.Provider.Dialect);
        var seen = new HashSet<string>();
        var current = entry.Value;
        for (var depth = 0; current is not null && depth < 10_000; depth++)
        {
            if (K(current) == K(self))
                throw new ValidationException(parentName, "crearía un ciclo: el registro quedaría como su propio ancestro");
            if (!seen.Add(K(current))) return; // ya hay un ciclo arriba, ajeno a este registro
            current = await db.ExecuteScalarAsync<object?>(Command(qb.BuildParentOf(table, current), ct, tx));
            if (current is DBNull) current = null;
        }
    }

    // ---------- Acciones (P-061) ----------

    /// <summary>Lo que devolvió la rutina: su primer conjunto de filas (hasta 200).</summary>
    public sealed record ActionResult(IReadOnlyList<IDictionary<string, object?>> Rows, bool Truncated);

    /// <summary>
    /// Ejecuta una acción de la tabla sobre un registro: arma los parámetros (columna del registro, valor fijo o lo que
    /// escribió el usuario) y llama a la rutina, que debe existir en el catálogo. Exige el permiso de ejecutar (D-088).
    /// </summary>
    public async Task<ActionResult> ExecuteActionAsync(string conn, string schema, string table, string actionName,
        IReadOnlyDictionary<string, string> key, IReadOnlyDictionary<string, JsonElement>? asked, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        Require(info, DataOperation.Execute, "ejecutar acciones en");
        var action = info.Actions.FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase))
                     ?? throw new NotFoundException($"{info.FullName} no tiene la acción '{actionName}'.");
        var routines = await schemas.GetRoutinesAsync(conn, ct);
        if (OverrideApplier.ValidateActions([action], routines).Values.FirstOrDefault() is { } problem)
            throw new OperationNotAllowedException($"La acción '{action.Name}' no se puede ejecutar: {problem}.");
        var routine = routines.First(r => string.Equals(r.FullName, action.Routine, StringComparison.OrdinalIgnoreCase));

        var keyValues = ParseKey(info, key);
        IReadOnlyDictionary<string, ActionParameter> given = action.Parameters ?? new Dictionary<string, ActionParameter>();
        IDictionary<string, object?>? row = null;
        var values = new List<object?>();
        var errors = new Dictionary<string, string>();
        foreach (var p in routine.Parameters)
        {
            var source = given.First(kv => string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase)).Value;
            try
            {
                if (source.Column is { Length: > 0 } col)
                {
                    var column = info.FindColumn(col) ?? throw new NotFoundException($"La columna '{col}' no existe.");
                    // Una columna que el usuario no puede ver tampoco se usa por él (D-067).
                    if (column.Hidden && !column.IsPrimaryKey)
                        throw new ForbiddenException($"La acción usa {col}, una columna que usted no puede ver.");
                    row ??= await GetByKeyAsync(ctx, info, keyValues, ct);
                    values.Add(row.TryGetValue(column.Name, out var v) ? v : null);
                }
                else if (source.Ask == true)
                    values.Add(asked is not null && asked.TryGetValue(p.Name, out var el) ? ValueConverter.FromJson(p.AsColumn(), el) : null);
                else
                    values.Add(ValueConverter.FromString(p.AsColumn(), source.Value));
            }
            catch (ValidationException ex) { errors[p.Name] = string.Join(" ", ex.Errors.Values); }
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var call = new QueryBuilder(ctx.Provider.Dialect).BuildCall(routine, values);
        List<IDictionary<string, object?>> rows;
        await using (var db = await OpenAsync(ctx, ct))
            rows = (await Translated(ctx, info, DbOperation.Update, () => db.QueryAsync(Command(call, ct))))
                .Cast<IDictionary<string, object?>>().Take(201).Select(r => (IDictionary<string, object?>)new Dictionary<string, object?>(r)).ToList();

        await AuditAsync(ctx, info, AuditOperation.Action, AuditValues.Key(info, keyValues.AsReadOnly()), null,
            AuditValues.Serialize(new Dictionary<string, object?>
            {
                ["action"] = action.Name, ["routine"] = routine.FullName,
                ["parameters"] = routine.Parameters.Select((p, i) => (p.Name, values[i])).ToDictionary(x => x.Name, x => x.Item2)
            }), ct);
        return new ActionResult(rows.Take(200).ToList(), rows.Count > 200);
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
        var (ctx, owner, link, bridge, target, localValue) = await ResolveLinkAsync(conn, schema, table, key, linkName, ct);
        Require(owner, DataOperation.Update, "editar registros de");
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

            var removed = current.Where(c => !wanted.ContainsKey(K(c))).ToList();
            var added = wanted.Where(w => !currentKeys.Contains(w.Key)).Select(w => w.Value).ToList();
            if (removed.Count > 0 || added.Count > 0)
                await AuditAsync(ctx, owner, AuditOperation.Links,
                    AuditValues.Key(owner, new Dictionary<string, object?> { [owner.PrimaryKey[0]] = localValue }.AsReadOnly()),
                    AuditValues.Serialize(new Dictionary<string, object?> { [link.Name] = current }),
                    AuditValues.Serialize(new Dictionary<string, object?> { [link.Name] = wanted.Values.ToList() }), ct);
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
            // PK compuesta (P-030): el valor es un objeto con cada columna de la clave.
            object? value = info.PrimaryKey.Count == 1
                ? d["value"]
                : info.PrimaryKey.Select((k, n) => (k, v: d[$"__k{n}"])).ToDictionary(x => x.k, x => (object?)x.v);
            return new LookupItem(value, d["label"]?.ToString());
        }).ToList();
    }

    /// <summary>
    /// Texto a mostrar de varios valores de <paramref name="column"/> en la tabla (P-094), con los permisos del usuario.
    /// Los valores vienen como JSON (los de la auditoría); la clave del resultado es su texto invariante.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> LabelsAsync(
        string conn, string schema, string table, string column, IReadOnlyCollection<JsonElement> values, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        if (!info.Access.HasFlag(DataOperation.View) || info.FindColumn(column) is not { } col) return new Dictionary<string, string>();
        var typed = new List<object?>();
        foreach (var v in values.Take(500))
        {
            try { if (ValueConverter.FromJson(col, v) is { } x) typed.Add(x); }
            catch (ValidationException) { /* un valor que ya no calza con el tipo: se muestra sin etiqueta */ }
        }
        if (new QueryBuilder(ctx.Provider.Dialect).BuildLabels(info, col.Name, typed) is not { } query) return new Dictionary<string, string>();
        await using var db = await OpenAsync(ctx, ct);
        var rows = await Translated(ctx, info, DbOperation.Read, () => db.QueryAsync(Command(query, ct)));
        var result = new Dictionary<string, string>();
        foreach (IDictionary<string, object?> r in rows)
            if (r["label"] is { } label)
                result[Convert.ToString(r["value"], System.Globalization.CultureInfo.InvariantCulture) ?? ""] = label.ToString()!;
        return result;
    }

    /// <summary>
    /// Escritura de un registro existente. Si el cliente dice qué versión editó (D-069), en una transacción: se bloquea la
    /// fila, se compara la versión y se escribe, sin la ventana de milisegundos entre comparar y escribir (P-096).
    /// Devuelve cómo estaba el registro antes, si se pidió (para la auditoría).
    /// </summary>
    private async Task<IDictionary<string, object?>?> WriteAsync(ConnectionContext ctx, TableInfo info,
        IReadOnlyDictionary<string, object?> key, string? expectedVersion, bool needBefore,
        Func<DbConnection, DbTransaction?, Task> write, CancellationToken ct)
    {
        await using var db = await OpenAsync(ctx, ct);
        if (expectedVersion is null)
        {
            var before = needBefore ? await GetByKeyAsync(ctx, info, key, ct, db) : null;
            await write(db, null);
            return before;
        }
        await using var tx = await db.BeginTransactionAsync(ct);
        await Translated(ctx, info, DbOperation.Read, () => db.ExecuteScalarAsync(Command(new QueryBuilder(ctx.Provider.Dialect).BuildLockRow(info, key), ct, tx)));
        var current = await GetByKeyAsync(ctx, info, key, ct, db, tx);
        CheckVersion(current, expectedVersion);
        await write(db, tx);
        await tx.CommitAsync(ct);
        return current;
    }

    private async Task<IDictionary<string, object?>> GetByKeyAsync(
        ConnectionContext ctx, TableInfo info, IReadOnlyDictionary<string, object?> key, CancellationToken ct,
        DbConnection? open = null, DbTransaction? tx = null)
    {
        var query = new QueryBuilder(ctx.Provider.Dialect).BuildGetByKey(ctx.Schema, info, key);
        await using var own = open is null ? await OpenAsync(ctx, ct) : null;
        var db = open ?? own!;
        var row = await Translated(ctx, info, DbOperation.Read, () => db.QuerySingleOrDefaultAsync(Command(query, ct, tx)));
        return row is null
            ? throw new NotFoundException("El registro no existe.")
            : RowVersion.Stamp(info, Normalize(info, Rename(ToRow(row), query.Renames)));
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
    /// <summary>Tamaño máximo de un archivo (columna binaria) al guardar.</summary>
    private long MaxFileBytes => (long)Math.Max(1, options.FileMaxMegabytes) * 1024 * 1024;

    private static Dictionary<string, object?> ParseValues(TableInfo table, IReadOnlyDictionary<string, JsonElement> body, long maxFileBytes)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>();

        foreach (var (name, element) in body)
        {
            if (name.EndsWith(QueryBuilder.LabelSuffix, StringComparison.Ordinal)) continue;

            var col = table.FindColumn(name);
            if (col is null) { errors[name] = "la columna no existe"; continue; }
            if (col.IsReadOnly) continue;

            try
            {
                var value = ValueConverter.ForWrite(col, element);
                if (value is byte[] file && file.LongLength > maxFileBytes)
                    throw new ValidationException(col.Name, $"el archivo pesa más de {maxFileBytes / 1024 / 1024} MB (Crud:FileMaxMegabytes)");
                values[col.Name] = value;
            }
            catch (ValidationException ex) { foreach (var e in ex.Errors) errors[e.Key] = e.Value; }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        return values;
    }

    private async Task<DbConnection> OpenAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var db = ctx.Provider.CreateConnection(schemas.ConnectionStringFor(ctx.Definition));
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
