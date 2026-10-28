using Mantenedores.Core.Connections;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;
using StackExchange.Redis;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Caché compartida entre instancias (P-050, D-026, D-091), sección <c>Cache</c>. Por defecto <c>Memory</c>: cada
/// instancia guarda lo suyo y no hace falta nada más. Con <c>Redis</c>, el esquema leído del catálogo y los contadores de
/// límites de uso se comparten. Si Redis no responde, cada instancia sigue sola (esquema desde el catálogo, límites en
/// memoria) y lo deja en el log; nunca se corta una solicitud por la caché.
/// </summary>
public sealed class CacheOptions
{
    public const string Section = "Cache";

    /// <summary><c>Memory</c> (por defecto) o <c>Redis</c>.</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>Configuración de StackExchange.Redis ("redis:6379,password=…"). Acepta <c>${env:…}</c> y <c>${file:…}</c>.</summary>
    public string? Redis { get; set; }

    /// <summary>Prefijo de todas las claves, para compartir un Redis con otras apps o con otra copia del servicio.</summary>
    public string KeyPrefix { get; set; } = "mnt:";

    /// <summary>Cuánto vive el esquema en Redis. ↻ y la relectura periódica (D-078) lo reemplazan antes si cambió.</summary>
    public int SchemaMinutes { get; set; } = 60;

    /// <summary>Tope de espera de cada operación en Redis: si tarda más, se sigue sin la caché.</summary>
    public int TimeoutMs { get; set; } = 1000;

    public bool UsesRedis => string.Equals(Provider, "Redis", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Contador por ventana compartido entre instancias. <c>null</c> = no disponible ahora (se cuenta en memoria).</summary>
public interface IRateCounter
{
    ValueTask<long?> IncrementAsync(string key, int by, TimeSpan window, CancellationToken ct);
}

public static class SharedCacheSetup
{
    public static void AddSharedCache(this IServiceCollection services, CacheOptions o, SecretResolver secrets)
    {
        services.AddSingleton(o);
        if (!o.UsesRedis)
        {
            if (!string.Equals(o.Provider, "Memory", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cache:Provider '{o.Provider}' no existe: use Memory o Redis.");
            services.AddSingleton<ISchemaCache, NoSchemaCache>();
            return;
        }
        if (string.IsNullOrWhiteSpace(o.Redis))
            throw new InvalidOperationException("Cache:Provider es Redis pero falta Cache:Redis (p. ej. \"redis:6379\").");

        var config = ConfigurationOptions.Parse(secrets.Resolve(o.Redis));
        config.AbortOnConnectFail = false; // la app arranca aunque Redis no esté; se conecta cuando vuelva
        config.ConnectTimeout = Math.Max(o.TimeoutMs, 2000);
        config.SyncTimeout = config.AsyncTimeout = o.TimeoutMs;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(config));
        services.AddSingleton<RedisCache>();
        services.AddSingleton<ISchemaCache>(sp => sp.GetRequiredService<RedisCache>());
        services.AddSingleton<IRateCounter>(sp => sp.GetRequiredService<RedisCache>());
    }
}

public sealed class RedisCache(IConnectionMultiplexer redis, CacheOptions options, ILogger<RedisCache> log) : ISchemaCache, IRateCounter
{
    // INCRBY y, si es la primera de la ventana, EXPIRE: en un solo viaje y sin carrera entre instancias.
    private const string IncrementScript =
        "local n = redis.call('INCRBY', KEYS[1], ARGV[1]) " +
        "if n == tonumber(ARGV[1]) then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end " +
        "return n";

    private long _lastWarning;

    // Sin conexión no se intenta nada (se sigue sin la caché al instante); estos avisos dicen cuándo se cayó y cuándo volvió.
    private readonly bool _subscribed = Subscribe(redis, log);

    private static bool Subscribe(IConnectionMultiplexer redis, ILogger log)
    {
        redis.ConnectionFailed += (_, e) =>
            log.LogWarning(e.Exception, "Se perdió la conexión con Redis ({Endpoint}); cada instancia sigue con su memoria", e.EndPoint);
        redis.ConnectionRestored += (_, e) => log.LogInformation("Se recuperó la conexión con Redis ({Endpoint})", e.EndPoint);
        return true;
    }

    private IDatabase? Db => redis.IsConnected ? redis.GetDatabase() : null;

    public async Task<DatabaseSchema?> GetAsync(string key, CancellationToken ct)
    {
        if (Db is not { } db) return null;
        try
        {
            var value = await db.StringGetAsync(options.KeyPrefix + key);
            return value.IsNullOrEmpty ? null : SchemaJson.Deserialize((byte[])value!);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or System.Text.Json.JsonException)
        {
            Warn(ex, "leer el esquema");
            return null;
        }
    }

    public async Task SetAsync(string key, DatabaseSchema schema, CancellationToken ct)
    {
        if (Db is not { } db) return;
        try { await db.StringSetAsync(options.KeyPrefix + key, SchemaJson.Serialize(schema), TimeSpan.FromMinutes(Math.Max(1, options.SchemaMinutes))); }
        catch (Exception ex) when (ex is RedisException or TimeoutException) { Warn(ex, "guardar el esquema"); }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        if (Db is not { } db) return;
        try { await db.KeyDeleteAsync(options.KeyPrefix + key); }
        catch (Exception ex) when (ex is RedisException or TimeoutException) { Warn(ex, "borrar el esquema"); }
    }

    public async ValueTask<long?> IncrementAsync(string key, int by, TimeSpan window, CancellationToken ct)
    {
        if (Db is not { } db) return null;
        try
        {
            var n = await db.ScriptEvaluateAsync(IncrementScript, [options.KeyPrefix + key], [by, (long)window.TotalMilliseconds]);
            return (long)n;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            Warn(ex, "contar el límite de uso");
            return null;
        }
    }

    /// <summary>Una advertencia por minuto como mucho: con Redis caído, cada solicitud fallaría igual.</summary>
    private void Warn(Exception ex, string what)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarning);
        if (last != 0 && now - last < 60_000) return;
        if (Interlocked.CompareExchange(ref _lastWarning, now, last) != last) return;
        log.LogWarning(ex, "Redis no respondió al {What}; se sigue sin la caché compartida", what);
    }
}
