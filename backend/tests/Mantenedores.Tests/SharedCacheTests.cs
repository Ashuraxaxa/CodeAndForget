using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Mantenedores.Tests;

/// <summary>Caché compartida opcional (P-050, D-091). La prueba contra un Redis real está en las de integración.</summary>
public sealed class SharedCacheTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("cache-tests-").FullName;

    /// <summary>Contador en memoria que dos instancias de prueba comparten, como lo harían con Redis.</summary>
    private sealed class FakeCounter : IRateCounter
    {
        private readonly ConcurrentDictionary<string, long> _counts = new();
        public bool Down { get; set; }
        public void Reset() => _counts.Clear();

        public ValueTask<long?> IncrementAsync(string key, int by, TimeSpan window, CancellationToken ct) =>
            ValueTask.FromResult(Down ? null : (long?)_counts.AddOrUpdate(key, by, (_, n) => n + by));
    }

    private HttpClient Instance(FakeCounter counter, string name, params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", Path.Combine(_data, name));
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureTestServices(s => s.AddSingleton<IRateCounter>(counter));
        });
        return factory.CreateClient();
    }

    private static async Task<HttpStatusCode> BadLogin(HttpClient c) =>
        (await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "mala" })).StatusCode;

    /// <summary>
    /// El tope compartido cuenta por minuto de reloj (SharedWindowLimiter): si la prueba cruza el cambio de minuto,
    /// los intentos caen en dos ventanas y no llega al tope. Con menos de 15 s por delante se espera al minuto siguiente.
    /// </summary>
    private static async Task StartOfMinuteWindowAsync()
    {
        var intoMinute = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 60_000;
        if (intoMinute > 45_000) await Task.Delay(TimeSpan.FromMilliseconds(60_000 - intoMinute + 200));
    }

    [Fact]
    public async Task Con_contador_compartido_el_limite_es_uno_para_todas_las_instancias()
    {
        var counter = new FakeCounter();
        var a = Instance(counter, "a", ("RateLimit:LoginPerMinute", "3"));
        var b = Instance(counter, "b", ("RateLimit:LoginPerMinute", "3"));
        await BadLogin(a); await BadLogin(b); // arranca las dos instancias antes de contar
        counter.Reset();
        await StartOfMinuteWindowAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await BadLogin(a));
        Assert.Equal(HttpStatusCode.Unauthorized, await BadLogin(b));
        Assert.Equal(HttpStatusCode.Unauthorized, await BadLogin(a));
        var res = await b.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "mala" });
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode); // la cuarta, aunque esta instancia solo vio una
        Assert.True(res.Headers.RetryAfter?.Delta is { TotalSeconds: > 0 and <= 60 });
    }

    [Fact]
    public async Task Si_el_contador_compartido_no_responde_cuenta_cada_instancia()
    {
        var counter = new FakeCounter { Down = true };
        var a = Instance(counter, "a", ("RateLimit:LoginPerMinute", "2"));
        Assert.Equal(HttpStatusCode.Unauthorized, await BadLogin(a));
        Assert.Equal(HttpStatusCode.Unauthorized, await BadLogin(a));
        Assert.Equal(HttpStatusCode.TooManyRequests, await BadLogin(a)); // el tope sigue, en memoria
    }

    [Fact]
    public void Memory_es_el_valor_por_defecto_y_un_proveedor_desconocido_no_arranca()
    {
        var s = new ServiceCollection();
        s.AddSharedCache(new CacheOptions(), new SecretResolver());
        Assert.Contains(s, d => d.ServiceType == typeof(ISchemaCache) && d.ImplementationType == typeof(NoSchemaCache));
        Assert.DoesNotContain(s, d => d.ServiceType == typeof(IRateCounter));

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSharedCache(new CacheOptions { Provider = "Memcached" }, new SecretResolver()));
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSharedCache(new CacheOptions { Provider = "Redis" }, new SecretResolver()));
    }

    [Fact]
    public void El_esquema_ida_y_vuelta_por_json_queda_igual()
    {
        var schema = TestSchema.Build();
        var back = SchemaJson.Deserialize(SchemaJson.Serialize(schema))!;

        Assert.Equal(SchemaJson.Serialize(schema), SchemaJson.Serialize(back));
        Assert.Empty(SchemaDiff.Compare(SchemaSnapshot.From(schema), SchemaSnapshot.From(back)));
        var producto = back.FindTable("ventas", "producto")!;
        Assert.Equal(schema.FindTable("ventas", "producto")!.ForeignKeys.Count, producto.ForeignKeys.Count);
        Assert.Equal(schema.FindTable("ventas", "producto")!.Columns.Select(c => c.Type), producto.Columns.Select(c => c.Type));
    }

    [Fact]
    public void La_clave_no_lleva_la_contrasena_y_cambia_con_la_base_o_los_esquemas()
    {
        ConnectionDefinition Def(string cs, params string[] schemas) => new()
        {
            Id = "Ventas", Name = "Ventas", Engine = DatabaseEngine.Postgres, ConnectionString = cs, Schemas = schemas
        };
        var key = SchemaCacheKeys.For(Def("Host=db;Database=demo;Username=u;Password=secreta"));

        Assert.StartsWith("schema:v1:ventas:", key);
        Assert.Equal(key, SchemaCacheKeys.For(Def("Host=db;Database=demo;Username=u;Password=otra")));
        Assert.Equal(key, SchemaCacheKeys.For(Def("Host=db;Database=demo;Username=u;Pwd=otra")));
        Assert.NotEqual(key, SchemaCacheKeys.For(Def("Host=db;Database=otra;Username=u;Password=secreta")));
        Assert.NotEqual(key, SchemaCacheKeys.For(Def("Host=db;Database=demo;Username=u;Password=secreta", "ventas")));
        Assert.DoesNotContain("secreta", SchemaCacheKeys.WithoutSecrets("Host=db;Password=secreta"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
