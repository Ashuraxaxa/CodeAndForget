using System.Collections.Concurrent;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

public sealed record ConnectionContext(ConnectionDefinition Definition, IDatabaseProvider Provider, DatabaseSchema Schema);

/// <summary>Resultado de probar una conexión: tablas que ve y servidor (versión, ajustes).</summary>
public sealed record ConnectionTestResult(int Tables, ServerInfo Server);

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
