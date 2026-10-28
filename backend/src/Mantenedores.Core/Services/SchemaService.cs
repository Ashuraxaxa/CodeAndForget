using System.Collections.Concurrent;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Security;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

public sealed record ConnectionContext(ConnectionDefinition Definition, IDatabaseProvider Provider, DatabaseSchema Schema);

/// <summary>Resultado de probar una conexión: tablas que ve y servidor (versión, ajustes).</summary>
public sealed record ConnectionTestResult(int Tables, ServerInfo Server);

/// <summary>
/// Cambios del esquema desde la foto de referencia (P-013). <c>Since</c>: cuándo se tomó esa foto.
/// <c>OverrideWarnings</c>: ajustes que quedaron sin efecto (p. ej. apuntan a una columna eliminada).
/// </summary>
public sealed record SchemaDiffResult(
    DateTimeOffset Since, string? AcceptedBy, IReadOnlyList<SchemaChange> Changes, IReadOnlyList<string> OverrideWarnings);

/// <summary>
/// Lee el esquema de cada conexión, le aplica los ajustes (P-011) y guarda ambos en memoria:
/// el leído del catálogo (caro: va a la base) y el combinado (barato: se rehace al cambiar un ajuste,
/// sin volver a leer la base). Con varias instancias, cada una tiene su caché y se avisan los cambios con
/// <see cref="ChangeStamps"/> (D-077).
/// </summary>
public sealed class SchemaService(
    IConnectionStore store, ProviderRegistry providers, IOverrideStore? overrides = null, ISchemaSnapshotStore? snapshots = null,
    SecretResolver? secrets = null, IAccessContext? access = null, PoolOptions? pool = null, ChangeStamps? stamps = null)
{
    private readonly ChangeStamps _stamps = stamps ?? new ChangeStamps();

    private readonly SecretResolver _secrets = secrets ?? new SecretResolver();
    private readonly PoolOptions _pool = pool ?? new PoolOptions();
    private readonly IAccessContext _access = access ?? new UnrestrictedAccess();

    /// <summary>Permisos del usuario de la solicitud actual (D-063).</summary>
    public ValueTask<UserAccess> AccessAsync(CancellationToken ct) => _access.CurrentAsync(ct);

    /// <summary>
    /// Cadena para conectar: secretos ya reemplazados (P-045) y tope del pool (D-070). La misma cadena da el mismo pool,
    /// por eso el resultado es estable para una misma definición.
    /// </summary>
    public string ConnectionStringFor(ConnectionDefinition def)
    {
        var resolved = _secrets.Resolve(def.ConnectionString);
        // Pocas conexiones y cadenas cortas: la caché evita reprocesarla en cada apertura.
        return _pooled.GetOrAdd((def.Engine, resolved), k => providers.Get(k.Engine).ApplyPool(k.Resolved, _pool));
    }

    private readonly ConcurrentDictionary<(DatabaseEngine Engine, string Resolved), string> _pooled = new();

    private readonly IOverrideStore _overrides = overrides ?? new NoOverrideStore();
    private readonly ISchemaSnapshotStore _snapshots = snapshots ?? new NoSnapshotStore();
    private readonly ConcurrentDictionary<string, Lazy<Task<DatabaseSchema>>> _raw = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<DatabaseSchema>>> _applied = new(StringComparer.OrdinalIgnoreCase);

    public IOverrideStore Overrides => _overrides;
    public ISchemaSnapshotStore Snapshots => _snapshots;

    /// <summary>
    /// Diferencias entre la foto de referencia y el esquema leído (el de la caché; tras "refrescar", el nuevo).
    /// Si la conexión aún no tiene foto, la de ahora pasa a serlo y no hay cambios.
    /// </summary>
    public async Task<SchemaDiffResult> GetDiffAsync(string connectionId, CancellationToken ct)
    {
        var raw = await GetRawSchemaAsync(connectionId, ct);
        var current = SchemaSnapshot.From(raw);
        var stored = await _snapshots.GetAsync(connectionId, ct);
        if (stored is null)
        {
            await _snapshots.SaveAsync(connectionId, current, null, ct);
            stored = new StoredSnapshot(current, DateTimeOffset.UtcNow, null);
        }
        var ctx = await GetUnrestrictedContextAsync(connectionId, ct);
        var applied = ctx.Schema;
        var changes = SchemaDiff.Compare(stored.Snapshot, current);
        var access = await _access.CurrentAsync(ct);
        if (access.Unrestricted)
            return new SchemaDiffResult(stored.TakenAt, stored.TakenBy, changes, applied.OverrideWarnings);

        // Con perfiles (D-067): solo los cambios de lo que el usuario puede ver, y sin los avisos de ajustes.
        var id = ctx.Definition.Id;
        if (!access.CanSeeConnection(id)) throw new NotFoundException($"La conexión '{connectionId}' no existe.");
        var visible = changes.Where(c => access.Table(id, c.Schema, c.Table).HasFlag(DataOperation.View) &&
                                         (c.Column is null || access.Column(id, c.Schema, c.Table, c.Column).HasFlag(DataOperation.View)))
            .ToList();
        return new SchemaDiffResult(stored.TakenAt, stored.TakenBy, visible, []);
    }

    /// <summary>Da por revisados los cambios: el esquema actual pasa a ser la referencia.</summary>
    public async Task<SchemaDiffResult> AcceptSchemaAsync(string connectionId, string? user, CancellationToken ct)
    {
        var raw = await GetRawSchemaAsync(connectionId, ct);
        await _snapshots.SaveAsync(connectionId, SchemaSnapshot.From(raw), user, ct);
        return await GetDiffAsync(connectionId, ct);
    }

    /// <summary>
    /// Conexión y esquema como los ve el usuario actual (D-063): sin las tablas que no puede ver y con sus
    /// columnas restringidas. Todo el CRUD pasa por aquí, así los permisos se cumplen en un solo lugar.
    /// </summary>
    public async Task<ConnectionContext> GetContextAsync(string connectionId, CancellationToken ct)
    {
        var ctx = await GetUnrestrictedContextAsync(connectionId, ct);
        var access = await _access.CurrentAsync(ct);
        if (!access.Unrestricted && !access.CanSeeConnection(ctx.Definition.Id))
            throw new NotFoundException($"La conexión '{connectionId}' no existe.");
        return ctx with { Schema = access.Restrict(ctx.Definition.Id, ctx.Schema) };
    }

    /// <summary>Esquema con ajustes, sin restricciones de usuario: para configurar mantenedores y avisar cambios.</summary>
    public async Task<ConnectionContext> GetUnrestrictedContextAsync(string connectionId, CancellationToken ct)
    {
        var (def, provider) = await DefinitionAsync(connectionId, ct);
        var schema = await Cached(_applied, def.Id, async () =>
        {
            var raw = await Cached(_raw, def.Id, () => ReadAsync(def, provider, CancellationToken.None), CancellationToken.None);
            return OverrideApplier.Apply(raw, await _overrides.ListAsync(def.Id, CancellationToken.None));
        }, ct);
        return new ConnectionContext(def, provider, schema);
    }

    /// <summary>El esquema tal como lo lee el catálogo, sin ajustes (para validarlos y para el editor).</summary>
    public async Task<DatabaseSchema> GetRawSchemaAsync(string connectionId, CancellationToken ct)
    {
        var (def, provider) = await DefinitionAsync(connectionId, ct);
        return await Cached(_raw, def.Id, () => ReadAsync(def, provider, CancellationToken.None), ct);
    }

    private async Task<(ConnectionDefinition, IDatabaseProvider)> DefinitionAsync(string connectionId, CancellationToken ct)
    {
        var def = await store.GetAsync(connectionId, ct)
                  ?? throw new NotFoundException($"La conexión '{connectionId}' no existe.");
        return (def, providers.Get(def.Engine));
    }

    private static async Task<DatabaseSchema> Cached(
        ConcurrentDictionary<string, Lazy<Task<DatabaseSchema>>> cache, string key, Func<Task<DatabaseSchema>> load, CancellationToken ct)
    {
        var lazy = cache.GetOrAdd(key, _ => new Lazy<Task<DatabaseSchema>>(load));
        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        catch when (lazy.Value.IsFaulted)
        {
            // No se guarda en caché un error: el siguiente intento vuelve a leer.
            cache.TryRemove(new KeyValuePair<string, Lazy<Task<DatabaseSchema>>>(key, lazy));
            throw;
        }
    }

    public async Task<(ConnectionContext Context, TableInfo Table)> GetTableAsync(
        string connectionId, string schema, string table, CancellationToken ct)
    {
        var ctx = await GetContextAsync(connectionId, ct);
        var info = ctx.Schema.FindTable(schema, table)
                   ?? throw new NotFoundException($"La tabla '{schema}.{table}' no existe en '{connectionId}'.");
        return (ctx, info);
    }

    public async Task<DatabaseSchema> RefreshAsync(string connectionId, CancellationToken ct)
    {
        await SchemaChangedAsync(connectionId, ct);
        return (await GetContextAsync(connectionId, ct)).Schema;
    }

    /// <summary>Solo en esta instancia. Para avisar a las demás, <see cref="SchemaChangedAsync"/>.</summary>
    public void Invalidate(string connectionId)
    {
        _raw.TryRemove(connectionId, out _);
        _applied.TryRemove(connectionId, out _);
        _routines.TryRemove(connectionId, out _);
    }

    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<RoutineInfo>>>> _routines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Procedimientos y funciones de la conexión (P-061), en caché como el esquema; ↻ los vuelve a leer.</summary>
    public async Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(string connectionId, CancellationToken ct)
    {
        var (def, provider) = await DefinitionAsync(connectionId, ct);
        var lazy = _routines.GetOrAdd(def.Id, _ => new Lazy<Task<IReadOnlyList<RoutineInfo>>>(async () =>
        {
            await using var conn = await OpenAsync(def, provider, CancellationToken.None);
            return await provider.Schema.ReadRoutinesAsync(conn, def.Schemas, CancellationToken.None);
        }));
        try { return await lazy.Value.WaitAsync(ct); }
        catch when (lazy.Value.IsFaulted)
        {
            _routines.TryRemove(new KeyValuePair<string, Lazy<Task<IReadOnlyList<RoutineInfo>>>>(def.Id, lazy));
            throw;
        }
    }

    /// <summary>Solo en esta instancia: se rehace el combinado sin volver a leer la base.</summary>
    public void InvalidateOverrides(string connectionId) => _applied.TryRemove(connectionId, out _);

    /// <summary>La conexión o su esquema cambió: se vuelve a leer aquí y en las demás instancias.</summary>
    public async Task SchemaChangedAsync(string connectionId, CancellationToken ct)
    {
        Invalidate(connectionId);
        await _stamps.PublishAsync(ChangeStamps.Schema(connectionId), ct);
    }

    /// <summary>Tras guardar o borrar un ajuste: se rehace el combinado aquí y en las demás instancias.</summary>
    public async Task OverridesChangedAsync(string connectionId, CancellationToken ct)
    {
        InvalidateOverrides(connectionId);
        await _stamps.PublishAsync(ChangeStamps.Overrides(connectionId), ct);
    }

    /// <summary>Conexiones cuyo esquema ya está en memoria (las que alguien abrió).</summary>
    public IReadOnlyList<string> LoadedConnections =>
        _raw.Where(kv => kv.Value.IsValueCreated && kv.Value.Value.IsCompletedSuccessfully).Select(kv => kv.Key).ToList();

    /// <summary>
    /// Relectura periódica (P-086): vuelve a leer el catálogo y, solo si algo cambió (tablas, columnas, tipos, claves),
    /// reemplaza el esquema en memoria. Así el aviso de cambios aparece sin que nadie pulse ↻.
    /// Devuelve los cambios encontrados (vacío si no hubo o si la conexión no estaba en memoria).
    /// </summary>
    public async Task<IReadOnlyList<SchemaChange>> ReloadIfChangedAsync(string connectionId, CancellationToken ct)
    {
        if (!_raw.TryGetValue(connectionId, out var lazy) || !lazy.IsValueCreated || !lazy.Value.IsCompletedSuccessfully) return [];
        var before = await lazy.Value;
        var (def, provider) = await DefinitionAsync(connectionId, ct);
        var fresh = await ReadAsync(def, provider, ct);
        var changes = SchemaDiff.Compare(SchemaSnapshot.From(before), SchemaSnapshot.From(fresh));
        if (changes.Count == 0) return [];
        _raw[connectionId] = new Lazy<Task<DatabaseSchema>>(Task.FromResult(fresh));
        _applied.TryRemove(connectionId, out _);
        await _stamps.PublishAsync(ChangeStamps.Schema(connectionId), ct);
        return changes;
    }

    /// <summary>Aviso de otra instancia (ver <see cref="ChangeStamps"/>). Devuelve si el ámbito era de esquema o ajustes.</summary>
    public bool ApplyRemoteChange(string scope)
    {
        if (scope.StartsWith("schema:", StringComparison.Ordinal)) { Invalidate(scope["schema:".Length..]); return true; }
        if (scope.StartsWith("overrides:", StringComparison.Ordinal)) { InvalidateOverrides(scope["overrides:".Length..]); return true; }
        return false;
    }

    /// <summary>
    /// Prueba una conexión sin guardarla: abre, verifica la versión y lee el catálogo.
    /// Si la versión es menor que la mínima, devuelve el servidor sin leer el esquema.
    /// </summary>
    public async Task<ConnectionTestResult> TestAsync(ConnectionDefinition def, CancellationToken ct)
    {
        var provider = providers.Get(def.Engine);
        await using var conn = await OpenAsync(def, provider, ct);
        var server = await provider.GetServerInfoAsync(conn, ct);
        if (!server.IsSupported) return new ConnectionTestResult(0, server);
        var schema = await provider.Schema.ReadAsync(conn, def.Schemas, ct);
        return new ConnectionTestResult(schema.Tables.Count, server);
    }

    private async Task<DatabaseSchema> ReadAsync(ConnectionDefinition def, IDatabaseProvider provider, CancellationToken ct)
    {
        await using var conn = await OpenAsync(def, provider, ct);
        var server = await provider.GetServerInfoAsync(conn, ct);
        if (!server.IsSupported)
            throw new UnsupportedServerException(UnsupportedMessage(server));
        return await provider.Schema.ReadAsync(conn, def.Schemas, ct);
    }

    public static string UnsupportedMessage(ServerInfo server) =>
        $"{server.Description} no está soportado: la versión mínima es {server.Product} {server.MinimumVersion} " +
        "(ver \"Requisitos de instalación\" en el README).";

    private async Task<System.Data.Common.DbConnection> OpenAsync(ConnectionDefinition def, IDatabaseProvider provider, CancellationToken ct)
    {
        var conn = provider.CreateConnection(ConnectionStringFor(def));
        await conn.OpenAsync(ct);
        await provider.InitializeSessionAsync(conn, ct);
        return conn;
    }
}
