using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mantenedores.Api.Licensing;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Licencias y planes (P-116, D-106), con la metadata por defecto (SQLite).</summary>
public sealed class LicenseTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("license-tests-").FullName;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    /// <summary>Sin clave = plan Gratis (la variable de las pruebas trae Empresa; aquí se reemplaza).</summary>
    private HttpClient Client(string key = "", params (string Key, string Value)[] settings)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            b.UseSetting("RateLimit:Enabled", "false");
            b.UseSetting("License:Key", key);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });
        _factories.Add(f);
        return f.CreateClient();
    }

    private static async Task<HttpClient> As(HttpClient c, string user = "admin", string password = "admin")
    {
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private static async Task AssertPlanLimit(HttpResponseMessage res, string contains)
    {
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mantenedores:plan", body.GetProperty("type").GetString());
        Assert.Contains(contains, body.GetProperty("detail").GetString());
    }

    [Fact]
    public void La_clave_firmada_se_valida_y_no_se_puede_alterar_ni_inventar()
    {
        var key = TestLicense.Issue(Plan.Pro, users: 40, org: "Andes SAC");
        var info = LicenseKey.Verify(key, TrustedLicenseKeys.All);
        Assert.Equal(("Andes SAC", Plan.Pro, 40), (info.Organization, info.Plan, info.Users));

        // Cambiar el plan en los datos rompe la firma.
        var parts = key.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight((parts[1].Length + 3) / 4 * 4, '=')));
        var forged = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json.Replace("\"pro\"", "\"business\""))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<ValidationException>(() => LicenseKey.Verify($"{parts[0]}.{forged}.{parts[2]}", TrustedLicenseKeys.All));

        // Firmada con otra llave (la de alguien que se hizo la suya).
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fake = LicenseKey.Issue(info with { Plan = Plan.Business }, other);
        Assert.Throws<ValidationException>(() => LicenseKey.Verify(fake, TrustedLicenseKeys.All));
        Assert.Throws<ValidationException>(() => LicenseKey.Verify("cualquier cosa", TrustedLicenseKeys.All));
    }

    [Fact]
    public async Task Gratis_permite_2_usuarios_y_1_conexion()
    {
        var admin = await As(Client());
        var plan = await admin.GetFromJsonAsync<JsonElement>("/api/license");
        Assert.Equal("Free", plan.GetProperty("plan").GetString());
        Assert.Equal(2, plan.GetProperty("limits").GetProperty("users").GetInt32());

        // admin (de la configuración) + ana = 2: el tercero no entra.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/security/users/ana", new { password = "clave-de-ana" })).StatusCode);
        await AssertPlanLimit(await admin.PutAsJsonAsync("/api/security/users/beto", new { password = "clave-de-beto" }), "2 usuarios");

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/connections/ventas",
            new { id = "ventas", name = "Ventas", engine = "Postgres", connectionString = "Host=nadie;Database=x" })).StatusCode);
        // Editar la misma sí; una segunda, no.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/connections/ventas",
            new { id = "ventas", name = "Ventas 2", engine = "Postgres", connectionString = "Host=nadie;Database=x" })).StatusCode);
        await AssertPlanLimit(await admin.PutAsJsonAsync("/api/connections/compras",
            new { id = "compras", name = "Compras", engine = "Postgres", connectionString = "Host=nadie;Database=y" }), "1 conexión");

        plan = await admin.GetFromJsonAsync<JsonElement>("/api/license");
        Assert.Equal(2, plan.GetProperty("usage").GetProperty("users").GetInt32());
        Assert.Equal("ventas", plan.GetProperty("freeConnection").GetString());
        Assert.True(plan.GetProperty("needsTableSelection").GetBoolean());
    }

    [Fact]
    public async Task Al_bajar_de_plan_quien_no_tiene_lugar_no_entra()
    {
        // Con Pro se crean 3 usuarios; sin licencia (Gratis) solo los 2 primeros tienen lugar.
        var pro = await As(Client(TestLicense.Issue(Plan.Pro)));
        foreach (var u in new[] { "ana", "beto" })
            Assert.Equal(HttpStatusCode.OK, (await pro.PutAsJsonAsync($"/api/security/users/{u}", new { password = $"clave-de-{u}" })).StatusCode);

        var free = Client();
        await As(free, "ana", "clave-de-ana"); // admin y ana tienen lugar
        var beto = await free.PostAsJsonAsync("/api/auth/login", new { username = "beto", password = "clave-de-beto" });
        await AssertPlanLimit(beto, "2 usuarios");
    }

    [Fact]
    public async Task Las_funciones_Pro_se_rechazan_en_Gratis_con_un_mensaje_claro()
    {
        var admin = await As(Client());
        await AssertPlanLimit(await admin.GetAsync("/api/data/ventas/ventas/producto/export?format=xlsx"), "Excel, JSON y XML");
        await AssertPlanLimit(await admin.PostAsJsonAsync("/api/meta/ventas/overrides/copy", new { from = "otra" }), "Copiar ajustes");
        await AssertPlanLimit(await admin.PostAsJsonAsync("/api/tokens", new { name = "Claude" }), "IA por MCP");
        await AssertPlanLimit(await admin.PutAsJsonAsync("/api/security/profiles/lectura", new { name = "Lectura", rules = Array.Empty<object>() }), "Perfiles y permisos");
        await AssertPlanLimit(await admin.GetAsync("/api/audit/export?format=xlsx"), "Excel, JSON y XML");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit/export?format=csv")).StatusCode);

        // Con Pro, lo mismo pasa el control del plan (y sigue a lo suyo).
        var pro = await As(Client(TestLicense.Issue(Plan.Pro)));
        Assert.Equal(HttpStatusCode.OK, (await pro.PostAsJsonAsync("/api/tokens", new { name = "Claude" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pro.GetAsync("/api/audit/export?format=xlsx")).StatusCode);
    }

    [Fact]
    public async Task Se_pega_una_licencia_y_la_prueba_Pro_se_usa_una_vez()
    {
        var admin = await As(Client());
        var trial = await admin.PostAsync("/api/license/trial", null);
        Assert.Equal(HttpStatusCode.OK, trial.StatusCode);
        var plan = await trial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Pro", "trial"), (plan.GetProperty("plan").GetString(), plan.GetProperty("source").GetString()));
        Assert.False(plan.GetProperty("trialAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/license/trial", null)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/license", new { key = "CELDO1.inventada.firma" })).StatusCode);
        var put = await admin.PutAsJsonAsync("/api/license", new { key = TestLicense.Issue(Plan.Business, org: "Pacífico SA") });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        plan = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Business", "stored", "Pacífico SA"),
            (plan.GetProperty("plan").GetString(), plan.GetProperty("source").GetString(), plan.GetProperty("organization").GetString()));
        Assert.True(plan.GetProperty("features").GetProperty("sso").GetBoolean());

        // Otra instancia con la misma metadata la lee de ahí.
        var other = await As(Client());
        Assert.Equal("Business", (await other.GetFromJsonAsync<JsonElement>("/api/license")).GetProperty("plan").GetString());
    }

    [Fact]
    public async Task Vencida_tiene_14_dias_de_gracia_y_despues_vuelve_a_Gratis()
    {
        var grace = await (await As(Client(TestLicense.Issue(Plan.Pro, expires: DateTimeOffset.UtcNow.AddDays(-3))))).GetFromJsonAsync<JsonElement>("/api/license");
        Assert.Equal("Pro", grace.GetProperty("plan").GetString());
        Assert.True(grace.GetProperty("inGrace").GetBoolean());

        var expired = await (await As(Client(TestLicense.Issue(Plan.Pro, expires: DateTimeOffset.UtcNow.AddDays(-20))))).GetFromJsonAsync<JsonElement>("/api/license");
        Assert.Equal("Free", expired.GetProperty("plan").GetString());
        Assert.Contains("venció", expired.GetProperty("licenseError").GetString());
    }

    [Fact]
    public async Task Las_funciones_Empresa_en_uso_sin_esa_licencia_detienen_la_API_pero_no_la_pantalla_Plan()
    {
        var c = await As(Client(TestLicense.Issue(Plan.Pro), ("Telemetry:OtlpEndpoint", "http://localhost:4317")));
        await AssertPlanLimit(await c.GetAsync("/api/connections"), "OpenTelemetry");
        var plan = await c.GetFromJsonAsync<JsonElement>("/api/license");
        Assert.Single(plan.GetProperty("requiresBusiness").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health")).StatusCode);

        var business = await As(Client(TestLicense.Issue(Plan.Business), ("Telemetry:OtlpEndpoint", "http://localhost:4317")));
        Assert.Equal(HttpStatusCode.OK, (await business.GetAsync("/api/connections")).StatusCode);
    }

    [Fact]
    public async Task Las_tablas_de_Gratis_se_suman_hasta_15_y_no_se_cambian()
    {
        var settings = new MemoryServiceSettings();
        var service = new LicenseService(settings, new OneConnection(), new Core.Security.NoSecurityStore(),
            Microsoft.Extensions.Options.Options.Create(new Api.Auth.AuthOptions()),
            Microsoft.Extensions.Options.Options.Create(new LicenseOptions()), []);
        var catalog = Enumerable.Range(1, 20).Select(i => $"ventas.t{i:00}").ToHashSet();

        Assert.True((await service.GetAsync(default)).NeedsTableSelection);
        await service.AddFreeTablesAsync(["ventas.t01", "VENTAS.T02"], catalog.Contains, default);
        var scope = await service.CurrentAsync(default);
        Assert.True(scope.AllowsTable("ventas", "t02"));
        Assert.False(scope.AllowsTable("ventas", "t03"));
        Assert.False(scope.AllowsConnection("otra"));
        Assert.Throws<PlanLimitException>(() => scope.EnsureTable("ventas", "t03"));

        // Se suman sin perder las anteriores; nunca más de 15.
        await service.AddFreeTablesAsync(catalog.Take(14).ToList(), catalog.Contains, default);
        Assert.Equal(14, (await service.GetAsync(default)).Selection.Tables.Count);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.AddFreeTablesAsync(["ventas.t18", "ventas.t19"], catalog.Contains, default));
        Assert.Contains("quedan 1", ex.Errors["tables"]);
        await Assert.ThrowsAsync<ValidationException>(() => service.AddFreeTablesAsync(["ventas.nada"], catalog.Contains, default));
        await service.AddFreeTablesAsync(["ventas.t20"], catalog.Contains, default);
        Assert.Equal(15, (await service.GetAsync(default)).Selection.Tables.Count);

        // Con Pro no hay selección que hacer: todas las tablas.
        var pro = new LicenseService(settings, new OneConnection(), new Core.Security.NoSecurityStore(),
            Microsoft.Extensions.Options.Options.Create(new Api.Auth.AuthOptions()),
            Microsoft.Extensions.Options.Options.Create(new LicenseOptions { Key = TestLicense.Issue(Plan.Pro) }), []);
        Assert.True((await pro.CurrentAsync(default)).AllowsTable("ventas", "t19"));
    }

    [Fact]
    public void La_linea_de_comandos_emite_y_lee_licencias()
    {
        var dir = Directory.CreateTempSubdirectory("license-cli-").FullName;
        var pem = Path.Combine(dir, "privada.pem");
        File.WriteAllText(pem, TestLicense.Key.ExportPkcs8PrivateKeyPem());
        var output = new StringWriter();
        Assert.Equal(0, LicenseCli.Run(["issue", "--key", pem, "--org", "Lima Data SAC", "--plan", "pro", "--users", "50", "--days", "365"], output, TextWriter.Null));
        var key = output.ToString().Trim();
        var show = new StringWriter();
        Assert.Equal(0, LicenseCli.Run(["show", key], show, TextWriter.Null));
        Assert.Contains("Lima Data SAC", show.ToString());
        Assert.Contains("Usuarios: 50", show.ToString());

        // keygen crea un par nuevo y no pisa uno existente.
        Assert.Equal(0, LicenseCli.Run(["keygen", Path.Combine(dir, "llaves")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, LicenseCli.Run(["keygen", Path.Combine(dir, "llaves")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(1, LicenseCli.Run(["issue"], TextWriter.Null, TextWriter.Null)); // faltan opciones
        Assert.Equal(2, LicenseCli.Run([], TextWriter.Null, TextWriter.Null)); // uso
        Directory.Delete(dir, recursive: true);
    }

    private sealed class OneConnection : Core.Connections.IConnectionStore
    {
        private static readonly Core.Connections.ConnectionDefinition Ventas = new()
        {
            Id = "ventas", Name = "Ventas", Engine = Core.Providers.DatabaseEngine.Postgres, ConnectionString = "Host=x"
        };
        public Task<IReadOnlyList<Core.Connections.ConnectionDefinition>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Core.Connections.ConnectionDefinition>>([Ventas]);
        public Task<Core.Connections.ConnectionDefinition?> GetAsync(string id, CancellationToken ct) => Task.FromResult<Core.Connections.ConnectionDefinition?>(id == "ventas" ? Ventas : null);
        public Task SaveAsync(Core.Connections.ConnectionDefinition connection, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(false);
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
