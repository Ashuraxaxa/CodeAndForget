using System.Threading.RateLimiting;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Services;
using Mantenedores.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Mantenedores.IntegrationTests;

/// <summary>Redis real como caché compartida (P-050, D-091): esquema y contadores de límites entre dos "instancias".</summary>
public sealed class RedisCacheTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Versión mínima probada; MANTENEDORES_REDIS_IMAGE prueba otra (p. ej. valkey/valkey:8).
    private static string Image => Environment.GetEnvironmentVariable("MANTENEDORES_REDIS_IMAGE") is { Length: > 0 } v ? v : "redis:6.2";

    private IContainer _redis = null!;
    private ConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        _redis = new ContainerBuilder(Image).WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync($"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}");
    }

    public async Task DisposeAsync()
    {
        await _mux.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private RedisCache Cache(string prefix = "t:") =>
        new(_mux, new CacheOptions { Provider = "Redis", KeyPrefix = prefix }, NullLogger<RedisCache>.Instance);

    private SchemaService Instance(string connectionString, RedisCache cache) =>
        new(new InMemoryConnectionStore(new ConnectionDefinition
        {
            Id = "demo", Name = "Demo", Engine = DatabaseEngine.Postgres, ConnectionString = connectionString, Schemas = ["ventas"]
        }), new ProviderRegistry([db.Provider]), shared: cache);

    [Fact]
    public async Task Una_instancia_lee_el_catalogo_y_la_otra_toma_el_esquema_de_redis()
    {
        var prefix = "t" + Guid.NewGuid().ToString("N")[..6] + ":";
        var a = Instance(db.ConnectionString, Cache(prefix));
        // La otra con una contraseña mala: si llegara a la base fallaría, así se ve que no fue.
        var b = Instance(db.WithCredentials("postgres", "mala"), Cache(prefix));

        var fromCatalog = await a.GetRawSchemaAsync("demo", default);
        var fromRedis = await b.GetRawSchemaAsync("demo", default);
        Assert.Equal(fromCatalog.Hash, fromRedis.Hash);
        Assert.NotNull(fromRedis.FindTable("ventas", "producto"));
        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync(prefix + SchemaCacheKeys.For((await new InMemoryConnectionStore(
            new ConnectionDefinition { Id = "demo", Name = "Demo", Engine = DatabaseEngine.Postgres, ConnectionString = db.ConnectionString, Schemas = ["ventas"] })
            .GetAsync("demo", default))!));
        Assert.True(ttl is { TotalMinutes: > 55 and <= 60 }, $"TTL {ttl}");

        // ↻ en una instancia: se borra de Redis y la otra, al recibir el aviso, ya no lo encuentra.
        await a.SchemaChangedAsync("demo", default);
        b.ApplyRemoteChange("schema:demo");
        await Assert.ThrowsAnyAsync<Exception>(() => b.GetRawSchemaAsync("demo", default));
    }

    [Fact]
    public async Task Los_limites_de_uso_suman_entre_instancias()
    {
        var options = new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
        var key = "rl:test:" + Guid.NewGuid().ToString("N");
        using var a = new SharedWindowLimiter(Cache(), key, options);
        using var b = new SharedWindowLimiter(Cache(), key, options);

        var granted = new List<bool>();
        foreach (var limiter in new[] { a, b, a, b })
        {
            Assert.False(limiter.AttemptAcquire().IsAcquired); // no cuenta: el middleware pasa a AcquireAsync
            using var lease = await limiter.AcquireAsync();
            granted.Add(lease.IsAcquired);
        }
        Assert.Equal([true, true, true, false], granted);
    }

    [Fact]
    public async Task Sin_redis_se_sigue_sin_cache_y_sin_esperar()
    {
        var down = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:1" }, AbortOnConnectFail = false, ConnectTimeout = 500
        });
        var cache = new RedisCache(down, new CacheOptions { Provider = "Redis" }, NullLogger<RedisCache>.Instance);
        var started = DateTime.UtcNow;

        Assert.Null(await cache.GetAsync("x", default));
        Assert.Null(await cache.IncrementAsync("x", 1, TimeSpan.FromMinutes(1), default));
        var schema = await Instance(db.ConnectionString, cache).GetRawSchemaAsync("demo", default); // desde el catálogo
        Assert.NotNull(schema.FindTable("ventas", "producto"));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));

        using var limiter = new SharedWindowLimiter(cache, "rl:down",
            new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        Assert.False((await limiter.AcquireAsync()).IsAcquired); // en memoria, el tope sigue
        await down.DisposeAsync();
    }
}
