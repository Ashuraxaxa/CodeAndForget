using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Providers;
using Mantenedores.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mantenedores.Tests;

/// <summary>Metadata del servicio en SQLite (P-010, D-023) y llaves en la base (D-025).</summary>
public sealed class MetadataStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("metadata-tests-").FullName;

    private static ConnectionDefinition Def(string id, string cs = "Host=srv;Database=ventas;Username=u;Password=secreta") => new()
    {
        Id = id, Name = "Ventas", Engine = DatabaseEngine.Postgres, ConnectionString = cs, Schemas = ["ventas", "rrhh"]
    };

    /// <summary>Servicios como en la app, a partir de una configuración en memoria.</summary>
    private ServiceProvider Services(string dataDir, params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddMetadataStore(config, dataDir);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Las_migraciones_se_aplican_una_sola_vez()
    {
        var db = new MetadataDatabase(new MetadataOptions(), _root);
        var first = await new MetadataMigrator(db).MigrateAsync();
        var second = await new MetadataMigrator(db).MigrateAsync();

        Assert.Equal(["001_conexiones", "002_overrides", "003_schema_snapshot", "004_security", "005_audit", "006_change_stamp", "007_tokens_propuestas", "008_ajustes_servicio"], first);
        Assert.Empty(second);
        Assert.True(File.Exists(Path.Combine(_root, "mantenedores.db")));
    }

    [Fact]
    public async Task Varias_instancias_migrando_a_la_vez_no_chocan()
    {
        // Como varias réplicas que arrancan juntas contra el mismo archivo (P-081).
        var options = new MetadataOptions();
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => new MetadataMigrator(new MetadataDatabase(options, _root)).MigrateAsync())));
        var all = runs.SelectMany(r => r).ToList();
        Assert.Equal(all.Distinct().Count(), all.Count); // ningún script se aplicó dos veces
        Assert.Equal(MetadataMigrator.Scripts("Sqlite").Count, all.Count);
    }

    [Fact]
    public async Task Guarda_lee_actualiza_y_borra_con_la_cadena_cifrada()
    {
        await using var sp = Services(_root);
        await sp.InitializeMetadataAsync(_root);
        var store = sp.GetRequiredService<IConnectionStore>();

        await store.SaveAsync(Def("Ventas"), default);
        var got = await store.GetAsync("VENTAS", default);
        Assert.Equal("ventas", got!.Id); // se guarda en minúsculas
        Assert.Equal("Host=srv;Database=ventas;Username=u;Password=secreta", got.ConnectionString);
        Assert.Equal(["ventas", "rrhh"], got.Schemas);

        await using (var conn = await sp.GetRequiredService<MetadataDatabase>().OpenAsync(default))
        {
            var raw = await conn.ExecuteScalarAsync<string>("SELECT protected_connection_string FROM mnt_connection");
            Assert.DoesNotContain("secreta", raw); // nunca en texto plano
        }

        await store.SaveAsync(Def("ventas") with { Name = "Ventas 2" }, default);
        Assert.Equal("Ventas 2", Assert.Single(await store.ListAsync(default)).Name);

        Assert.True(await store.DeleteAsync("ventas", default));
        Assert.False(await store.DeleteAsync("ventas", default));
        Assert.Null(await store.GetAsync("ventas", default));
    }

    [Fact]
    public async Task Importa_el_archivo_de_la_fase_1_una_vez_y_con_llaves_en_la_base()
    {
        // Fase 1: archivo JSON con llaves en data/keys.
        await using (var legacy = Services(_root, ("Metadata:Provider", "File")))
        {
            var store = legacy.GetRequiredService<IConnectionStore>();
            await store.SaveAsync(Def("ventas"), default);
            await store.SaveAsync(Def("rrhh", "Server=x;Database=rrhh;User Id=u;Password=otra") with { Engine = DatabaseEngine.SqlServer }, default);
        }
        Assert.True(File.Exists(Path.Combine(_root, "connections.json")));

        // Fase 2: SQLite con llaves en la base. Al arrancar se importa y se vuelve a cifrar con las llaves nuevas.
        await using var sp = Services(_root, ("DataProtection:KeyStore", "Database"));
        await sp.InitializeMetadataAsync(_root);
        var imported = await sp.GetRequiredService<IConnectionStore>().ListAsync(default);

        Assert.Equal(["rrhh", "ventas"], imported.Select(c => c.Id));
        Assert.Equal("Server=x;Database=rrhh;User Id=u;Password=otra", imported[0].ConnectionString);
        Assert.Equal(DatabaseEngine.SqlServer, imported[0].Engine);
        Assert.False(File.Exists(Path.Combine(_root, "connections.json")));
        Assert.True(File.Exists(Path.Combine(_root, "connections.json.importado")));

        // Las llaves nuevas quedaron en la base, no en data/keys.
        await using var conn = await sp.GetRequiredService<MetadataDatabase>().OpenAsync(default);
        Assert.True(await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM mnt_dataprotection_key") > 0);
    }

    [Fact]
    public async Task Dos_instancias_comparten_conexiones_y_llaves()
    {
        // Misma base de metadata, carpetas de datos distintas: solo la base es común (como dos contenedores).
        var db = Path.Combine(_root, "compartida.db");
        HttpClient Instance(string name) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", Path.Combine(_root, name));
            b.UseSetting("Metadata:ConnectionString", $"Data Source={db}");
            b.UseSetting("DataProtection:KeyStore", "Database");
        }).CreateClient();

        var a = Instance("a");
        var b = Instance("b");
        foreach (var c in new[] { a, b })
        {
            var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
            var token = (await login.Content.ReadFromJsonAsync<Login>())!.Token;
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var save = await a.PutAsJsonAsync("/api/connections/ventas", new
        {
            id = "ventas", name = "Ventas", engine = "Postgres", connectionString = "Host=srv-a;Database=ventas;Username=u;Password=p"
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        // La instancia B descifra la cadena (muestra servidor y base): comparte las llaves.
        var list = await b.GetStringAsync("/api/connections");
        Assert.Contains("\"server\":\"srv-a\"", list);
        Assert.Contains("\"database\":\"ventas\"", list);
    }

    [Fact]
    public async Task Una_instancia_ve_al_momento_los_cambios_de_seguridad_de_otra()
    {
        // D-077: sin el aviso, B vería el perfil nuevo recién al vencer su caché (30 s) y el usuario desactivado a los 10 s.
        var db = Path.Combine(_root, "compartida.db");
        HttpClient Instance(string name) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", Path.Combine(_root, name));
            b.UseSetting("Metadata:ConnectionString", $"Data Source={db}");
            b.UseSetting("Metadata:SyncSeconds", "1");
        }).CreateClient();
        async Task Login(HttpClient c, string user, string password)
        {
            var login = await c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<Login>())!.Token);
        }
        async Task Eventually(Func<Task<bool>> condition)
        {
            for (var i = 0; i < 50 && !await condition(); i++) await Task.Delay(100);
            Assert.True(await condition());
        }

        var a = Instance("a");
        var b = Instance("b");
        var ana = Instance("b");
        await Login(a, "admin", "admin");
        await Login(b, "admin", "admin");
        Assert.Contains("\"securityOpen\":true", await b.GetStringAsync("/api/auth/me"));
        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync("/api/security/users/ana", new { password = "clave-de-ana" })).StatusCode);
        await Login(ana, "ana", "clave-de-ana");
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/auth/me")).StatusCode);
        await Task.Delay(1500); // que B haga su primera revisión (solo toma nota)

        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync("/api/security/profiles/lectura",
            new { name = "Lectura", users = new[] { "ana" }, connections = new { } })).StatusCode);
        await Eventually(async () => (await b.GetStringAsync("/api/auth/me")).Contains("\"securityOpen\":false"));

        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync("/api/security/users/ana", new { active = false })).StatusCode);
        await Eventually(async () => (await ana.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("File")]
    public async Task Ajustes_se_guardan_reemplazan_y_borran_por_conexion(string provider)
    {
        await using var sp = Services(_root, ("Metadata:Provider", provider));
        await sp.InitializeMetadataAsync(_root);
        var store = sp.GetRequiredService<IOverrideStore>();

        var o = new TableOverride
        {
            Label = "Productos",
            VirtualForeignKeys = [new VirtualForeignKey(["categoria_id"], "ventas", "categoria", ["id"])],
            Columns = new Dictionary<string, ColumnOverride> { ["precio"] = new() { Min = 0, Options = ["a"] } }
        };
        await store.SaveAsync("Ventas", "ventas", "Producto", o, "ana", default);
        await store.SaveAsync("ventas", "ventas", "categoria", new TableOverride { Hidden = true }, null, default);
        await store.SaveAsync("otra", "ventas", "producto", new TableOverride { Label = "Otra" }, null, default);

        var all = await store.ListAsync("VENTAS", default);
        Assert.Equal(2, all.Count);
        var got = all[("ventas", "Producto")]; // esquema y tabla tal como se guardaron
        Assert.Equal("Productos", got.Label);
        Assert.Equal("categoria", got.VirtualForeignKeys![0].RefTable);
        Assert.Equal(0m, got.Columns!["precio"].Min);
        Assert.Null(got.Hidden);

        await store.SaveAsync("ventas", "ventas", "Producto", new TableOverride { Label = "Nuevo" }, "ana", default);
        Assert.Equal("Nuevo", (await store.ListAsync("ventas", default))[("ventas", "Producto")].Label);

        Assert.True(await store.DeleteAsync("ventas", "ventas", "Producto", default));
        Assert.False(await store.DeleteAsync("ventas", "ventas", "Producto", default));
        await store.DeleteAllAsync("ventas", default);
        Assert.Empty(await store.ListAsync("ventas", default));
        Assert.Single(await store.ListAsync("otra", default)); // las otras conexiones no se tocan
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("File")]
    public async Task La_foto_del_esquema_se_guarda_y_reemplaza(string provider)
    {
        await using var sp = Services(_root, ("Metadata:Provider", provider));
        await sp.InitializeMetadataAsync(_root);
        var store = sp.GetRequiredService<Mantenedores.Core.Schema.ISchemaSnapshotStore>();
        var snapshot = Mantenedores.Core.Schema.SchemaSnapshot.From(TestSchema.Build());

        Assert.Null(await store.GetAsync("ventas", default));
        await store.SaveAsync("Ventas", snapshot, null, default);
        await store.SaveAsync("ventas", snapshot with { Tables = snapshot.Tables.Take(1).ToList() }, "ana", default);

        var got = await store.GetAsync("VENTAS", default);
        Assert.Single(got!.Snapshot.Tables);
        Assert.Equal("ana", got.TakenBy);
        Assert.True(DateTimeOffset.UtcNow - got.TakenAt < TimeSpan.FromMinutes(1));

        await store.DeleteAsync("ventas", default);
        Assert.Null(await store.GetAsync("ventas", default));
    }

    [Theory]
    [InlineData("Postgres", "", "Metadata:ConnectionString")]
    [InlineData("SqlServer", "", "Metadata:ConnectionString")]
    [InlineData("File", "Database", "DataProtection:KeyStore=Database")]
    public void La_configuracion_invalida_explica_que_falta(string provider, string keyStore, string expected)
    {
        var settings = new List<(string, string)> { ("Metadata:Provider", provider) };
        if (keyStore.Length > 0) settings.Add(("DataProtection:KeyStore", keyStore));
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var sp = Services(_root, [.. settings]);
            sp.GetRequiredService<IConnectionStore>();
        });
        Assert.Contains(expected, ex.Message);
    }

    private sealed record Login(string Token);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // libera el archivo para poder borrarlo
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
