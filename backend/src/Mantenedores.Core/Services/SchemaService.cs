using System.Collections.Concurrent;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

public sealed record ConnectionContext(ConnectionDefinition Definition, IDatabaseProvider Provider, DatabaseSchema Schema);

/// <summary>
/// Lee el esquema de cada conexión y lo guarda en memoria.
/// En Fase 4 esta caché pasa a Redis para compartirla entre instancias.
/// </summary>
public sealed class SchemaService(IConnectionStore store, ProviderRegistry providers)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<DatabaseSchema>>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ConnectionContext> GetContextAsync(string connectionId, CancellationToken ct)
    {
        var def = await store.GetAsync(connectionId, ct)
                  ?? throw new NotFoundException($"La conexión '{connectionId}' no existe.");
        var provider = providers.Get(def.Engine);

        var lazy = _cache.GetOrAdd(def.Id, _ => new Lazy<Task<DatabaseSchema>>(() => ReadAsync(def, provider, CancellationToken.None)));
        try
        {
            var schema = await lazy.Value.WaitAsync(ct);
            return new ConnectionContext(def, provider, schema);
        }
        catch when (lazy.Value.IsFaulted)
        {
            // No se guarda en caché un error: el siguiente intento vuelve a leer.
            _cache.TryRemove(new KeyValuePair<string, Lazy<Task<DatabaseSchema>>>(def.Id, lazy));
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

    public void Invalidate(string connectionId) => _cache.TryRemove(connectionId, out _);

    /// <summary>Prueba una conexión sin guardarla: abre, lee el catálogo y devuelve cuántas tablas ve.</summary>
    public async Task<int> TestAsync(ConnectionDefinition def, CancellationToken ct)
    {
        var schema = await ReadAsync(def, providers.Get(def.Engine), ct);
        return schema.Tables.Count;
    }

    private static async Task<DatabaseSchema> ReadAsync(ConnectionDefinition def, IDatabaseProvider provider, CancellationToken ct)
    {
        await using var conn = provider.CreateConnection(def.ConnectionString);
        await conn.OpenAsync(ct);
        return await provider.Schema.ReadAsync(conn, def.Schemas, ct);
    }
}
