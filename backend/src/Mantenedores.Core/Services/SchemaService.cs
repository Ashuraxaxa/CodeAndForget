using System.Collections.Concurrent;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Overrides;
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
/// sin volver a leer la base). En Fase 4 esta caché pasa a Redis para compartirla entre instancias.
/// </summary>
public sealed class SchemaService(
    IConnectionStore store, ProviderRegistry providers, IOverrideStore? overrides = null, ISchemaSnapshotStore? snapshots = null)
{
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
        var applied = (await GetContextAsync(connectionId, ct)).Schema;
        return new SchemaDiffResult(stored.TakenAt, stored.TakenBy, SchemaDiff.Compare(stored.Snapshot, current), applied.OverrideWarnings);
    }

    /// <summary>Da por revisados los cambios: el esquema actual pasa a ser la referencia.</summary>
    public async Task<SchemaDiffResult> AcceptSchemaAsync(string connectionId, string? user, CancellationToken ct)
    {
        var raw = await GetRawSchemaAsync(connectionId, ct);
        await _snapshots.SaveAsync(connectionId, SchemaSnapshot.From(raw), user, ct);
        return await GetDiffAsync(connectionId, ct);
    }

    public async Task<ConnectionContext> GetContextAsync(string connectionId, CancellationToken ct)
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
        Invalidate(connectionId);
        return (await GetContextAsync(connectionId, ct)).Schema;
    }

    public void Invalidate(string connectionId)
    {
        _raw.TryRemove(connectionId, out _);
        _applied.TryRemove(connectionId, out _);
    }

    /// <summary>Tras guardar o borrar un ajuste: se rehace el combinado sin volver a leer la base.</summary>
    public void InvalidateOverrides(string connectionId) => _applied.TryRemove(connectionId, out _);

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

    private static async Task<DatabaseSchema> ReadAsync(ConnectionDefinition def, IDatabaseProvider provider, CancellationToken ct)
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

    private static async Task<System.Data.Common.DbConnection> OpenAsync(ConnectionDefinition def, IDatabaseProvider provider, CancellationToken ct)
    {
        var conn = provider.CreateConnection(def.ConnectionString);
        await conn.OpenAsync(ct);
        await provider.InitializeSessionAsync(conn, ct);
        return conn;
    }
}
