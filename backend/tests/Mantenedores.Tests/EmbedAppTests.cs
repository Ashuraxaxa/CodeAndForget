using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Tests;

/// <summary>Aplicaciones que embeben con tokens firmados (P-135, D-113).</summary>
public sealed class EmbedAppTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("embed-apps-").FullName;
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    private WebApplicationFactory<Program>? _factory;

    private HttpClient Client(string? license = null)
    {
        if (_factory is null || license is not null)
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Storage:DataDirectory", _data);
                b.UseSetting("RateLimit:Enabled", "false");
                b.UseSetting("License:Key", license ?? TestLicense.Issue(Plan.Pro));
            });
            _factories.Add(_factory);
        }
        return _factory.CreateClient();
    }

    private async Task<HttpClient> Admin(string? license = null)
    {
        var c = Client(license);
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        login.EnsureSuccessStatusCode();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private static async Task Profile(HttpClient admin, string id = "portal") =>
        (await admin.PutAsJsonAsync($"/api/security/profiles/{id}", new
        {
            name = "Portal", dashboards = new[] { "view" },
            connections = new Dictionary<string, object> { ["demo"] = new { operations = new[] { "view", "create", "update" } } }
        })).EnsureSuccessStatusCode();

    private static async Task<(string Id, string Secret)> CreateApp(HttpClient admin, string name = "Portal", string origin = "https://portal.cliente.com")
    {
        var res = await admin.PostAsJsonAsync("/api/embed/apps", new { name, origins = new[] { origin }, profileId = "portal" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("app").GetProperty("id").GetString()!, body.GetProperty("secret").GetString()!);
    }

    /// <summary>Lo que haría el servidor de la otra app: un JWT HS256 con la clave y el id de la aplicación como kid.</summary>
    private static string Sign(string appId, string secret, Dictionary<string, object> claims, TimeSpan? life = null, DateTime? expires = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = appId };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            NotBefore = expires is null ? null : expires.Value.AddHours(-2),
            IssuedAt = expires is null ? null : expires.Value.AddHours(-2),
            Expires = expires ?? DateTime.UtcNow.Add(life ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });
    }

    private static Dictionary<string, object> Claims(params string[] res) => new()
    {
        ["sub"] = "juan@cliente.com", ["name"] = "Juan", ["res"] = res, ["perm"] = new[] { "view", "edit" }
    };

    private async Task<HttpResponseMessage> Exchange(string token) =>
        await Client().PostAsJsonAsync("/api/embed/session", new { token });

    private async Task<HttpClient> Session(string token)
    {
        var res = await Exchange(token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        Assert.StartsWith("emb_", session);
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return c;
    }

    [Fact]
    public async Task El_token_firmado_se_canjea_por_una_sesion_sin_permisos_de_la_app()
    {
        var admin = await Admin();
        await Profile(admin);
        var (id, secret) = await CreateApp(admin);
        Assert.StartsWith("csk_", secret);

        // La clave no vuelve a salir.
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/embed/apps");
        var app = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(secret, list.GetRawText());
        Assert.Equal(3, list.GetProperty("limit").GetInt32());

        var s = await Session(Sign(id, secret, Claims("table:demo/ventas/producto")));
        var me = await s.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("app:Portal/juan@cliente.com", me.GetProperty("name").GetString());
        Assert.False(me.GetProperty("isAdmin").GetBoolean());
        Assert.Empty(me.GetProperty("permissions").EnumerateArray());

        // Solo sus datos y dashboards: lo demás de la API no responde a una sesión embebida.
        foreach (var path in new[] { "/api/connections", "/api/security/profiles", "/api/tokens", "/api/audit", "/api/dashboards",
                     "/api/embed/apps", "/odata/demo/", "/api/meta/demo/diff" })
            Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.PostAsJsonAsync("/api/tokens", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.PostAsJsonAsync("/api/dashboards", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.GetAsync("/api/license")).StatusCode);

        // El sitio de la aplicación puede embeber /embed.
        var csp = (await Client().GetAsync("/embed/dashboards/x")).Headers.GetValues("Content-Security-Policy").Single();
        Assert.Equal("frame-ancestors 'self' https://portal.cliente.com", csp);
    }

    [Fact]
    public async Task Un_token_mal_firmado_vencido_o_de_otra_app_no_entra()
    {
        var admin = await Admin();
        await Profile(admin);
        var (id, secret) = await CreateApp(admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(Sign(id, "csk_otra-clave-de-32-caracteres-o-mas!!", Claims("table:demo/ventas/producto")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(Sign("no-existe", secret, Claims("table:demo/ventas/producto")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(Sign(id, secret, Claims("table:demo/ventas/producto"), expires: DateTime.UtcNow.AddMinutes(-5)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange("no.es.jwt")).StatusCode);

        // Bien firmado pero mal armado: 400 con el motivo.
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, Claims("table:demo/ventas/producto"), life: TimeSpan.FromHours(3)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, Claims()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, Claims("tabla:x")))).StatusCode);
        var withFilters = Claims("table:demo/ventas/producto");
        withFilters["filters"] = new Dictionary<string, object> { ["cliente_id"] = 42 };
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, withFilters))).StatusCode);
        var badPerm = Claims("table:demo/ventas/producto");
        badPerm["perm"] = new[] { "admin" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, badPerm))).StatusCode);
        var noSub = Claims("table:demo/ventas/producto");
        noSub.Remove("sub");
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, noSub))).StatusCode);
    }

    [Fact]
    public async Task Rotar_la_clave_o_desactivar_la_app_corta_las_sesiones()
    {
        var admin = await Admin();
        await Profile(admin);
        var (id, secret) = await CreateApp(admin);
        var token = Sign(id, secret, Claims("table:demo/ventas/producto"));
        var s = await Session(token);
        Assert.Equal(HttpStatusCode.OK, (await s.GetAsync("/api/auth/me")).StatusCode);

        var rotated = await (await admin.PostAsync($"/api/embed/apps/{id}/secret", null)).Content.ReadFromJsonAsync<JsonElement>();
        var newSecret = rotated.GetProperty("secret").GetString()!;
        Assert.NotEqual(secret, newSecret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(token)).StatusCode);

        var s2 = await Session(Sign(id, newSecret, Claims("table:demo/ventas/producto")));
        (await admin.PutAsJsonAsync($"/api/embed/apps/{id}", new { enabled = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s2.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/embed/apps/{id}")).StatusCode);
    }

    [Fact]
    public async Task Solo_ve_los_dashboards_del_token_compartidos_con_su_perfil()
    {
        var admin = await Admin();
        await Profile(admin);
        var (id, secret) = await CreateApp(admin);
        async Task<string> Dashboard(string name, bool share)
        {
            var created = await (await admin.PostAsJsonAsync("/api/dashboards", new { name })).Content.ReadFromJsonAsync<JsonElement>();
            var dashId = created.GetProperty("dashboard").GetProperty("id").GetString()!;
            if (share)
                (await admin.PutAsJsonAsync($"/api/dashboards/{dashId}", new { name, widgets = Array.Empty<object>(), shares = new[] { new { profile = "portal" } } }))
                    .EnsureSuccessStatusCode();
            return dashId;
        }
        var shared = await Dashboard("Ventas", share: true);
        var privado = await Dashboard("Privado", share: false);
        var otro = await Dashboard("Otro", share: true);

        var s = await Session(Sign(id, secret, Claims($"dashboard:{shared}", $"dashboard:{privado}")));
        Assert.Equal(HttpStatusCode.OK, (await s.GetAsync($"/api/dashboards/{shared}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await s.GetAsync($"/api/dashboards/{privado}")).StatusCode); // no compartido
        Assert.NotEqual(HttpStatusCode.OK, (await s.GetAsync($"/api/dashboards/{otro}")).StatusCode);    // no está en el token
        Assert.Equal(HttpStatusCode.Forbidden, (await s.PutAsJsonAsync($"/api/dashboards/{shared}", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(Sign(id, secret, Claims("dashboard:no-existe")))).StatusCode);
    }

    [Fact]
    public async Task El_plan_pone_el_tope_de_aplicaciones()
    {
        var free = await Admin(license: "");
        Assert.Equal(HttpStatusCode.Forbidden, (await free.PostAsJsonAsync("/api/embed/apps",
            new { name = "Portal", origins = new[] { "https://a.com" }, profileId = "portal" })).StatusCode);

        var pro = await Admin(TestLicense.Issue(Plan.Pro));
        await Profile(pro);
        for (var i = 1; i <= 3; i++) await CreateApp(pro, $"App {i}", $"https://app{i}.com");
        var fourth = await pro.PostAsJsonAsync("/api/embed/apps", new { name = "App 4", origins = new[] { "https://app4.com" }, profileId = "portal" });
        Assert.Equal(HttpStatusCode.Forbidden, fourth.StatusCode);
        Assert.Contains("3 aplicaciones", await fourth.Content.ReadAsStringAsync());

        // Validaciones: sitio, perfil y nombre repetido.
        Assert.Equal(HttpStatusCode.NotFound, (await pro.PutAsJsonAsync("/api/embed/apps/x", new { })).StatusCode);
        var list = await pro.GetFromJsonAsync<JsonElement>("/api/embed/apps");
        var first = list.GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await pro.PutAsJsonAsync($"/api/embed/apps/{first}", new { origins = new[] { "*" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pro.PutAsJsonAsync($"/api/embed/apps/{first}", new { profileId = "no-existe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await pro.PutAsJsonAsync($"/api/embed/apps/{first}", new { name = "App 2" })).StatusCode);
    }

    // ---------- El acceso de una sesión embebida (sin HTTP) ----------

    private static readonly SecurityProfile Portal = new()
    {
        Id = "portal", Name = "Portal", Dashboards = ["view", "create"],
        Connections = new Dictionary<string, ConnectionRule> { ["demo"] = new() { Operations = ["view", "create", "update", "delete", "export"] } }
    };

    private static UserAccess Embedded(DataOperation ops, params (string, string, string)[] tables) =>
        UserAccess.Embedded("app:Portal/juan", Portal, new EmbedScope("a1", ops,
            tables.Select(t => EmbedScope.TableKey(t.Item1, t.Item2, t.Item3)).ToHashSet(), new HashSet<string>()));

    [Fact]
    public void La_sesion_embebida_no_pasa_del_token_ni_del_perfil()
    {
        var a = Embedded(DataOperation.View | DataOperation.Update | DataOperation.Import, ("demo", "ventas", "producto"));
        // Del token: ver y editar; importar no, porque el perfil no lo da; borrar no, porque el token no lo pide.
        Assert.Equal(DataOperation.View | DataOperation.Update, a.Table("demo", "VENTAS", "Producto"));
        Assert.Equal(DataOperation.None, a.Table("demo", "ventas", "cliente")); // otra tabla del perfil, fuera del token
        Assert.Equal(DataOperation.None, a.Table("otra", "ventas", "producto"));
        Assert.True(a.CanSeeConnection("demo"));
        Assert.False(a.CanSeeConnection("otra"));
        Assert.False(a.Unrestricted);
        Assert.False(a.IsAdmin);
        Assert.Empty(a.AppPermissions);
        Assert.Equal(DataOperation.View, a.Dashboards); // el perfil deja crear dashboards, embebida solo se mira

        // Sin crear ni editar en el token, ninguna columna se escribe.
        var soloVer = Embedded(DataOperation.View, ("demo", "ventas", "producto"));
        Assert.Equal(DataOperation.View, soloVer.Column("demo", "ventas", "producto", "precio"));
        Assert.Equal(DataOperation.View | DataOperation.Update, a.Column("demo", "ventas", "producto", "precio"));

        // Si se borró el perfil de la aplicación, no ve nada (y nunca queda "abierta").
        var sinPerfil = UserAccess.Embedded("app:Portal/juan", null, new EmbedScope("a1", DataOperation.All,
            new HashSet<string> { EmbedScope.TableKey("demo", "ventas", "producto") }, new HashSet<string>()));
        Assert.False(sinPerfil.IsOpen);
        Assert.Equal(DataOperation.None, sinPerfil.Table("demo", "ventas", "producto"));
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
