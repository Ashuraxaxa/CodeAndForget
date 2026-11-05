using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mantenedores.Core.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Dashboards públicos por enlace secreto (P-137, D-116).</summary>
public sealed class PublicDashboardTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("public-dash-").FullName;
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

    private async Task<HttpClient> As(string user, string password, string? license = null)
    {
        var c = Client(license);
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        login.EnsureSuccessStatusCode();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private static async Task<string> NewDashboard(HttpClient c, string name = "Ventas")
    {
        var res = await c.PostAsJsonAsync("/api/dashboards", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("dashboard").GetProperty("id").GetString()!;
    }

    private static async Task<string> Publish(HttpClient c, string id)
    {
        var res = await c.PostAsync($"/api/dashboards/{id}/public", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isPublic").GetBoolean());
        return body.GetProperty("dashboard").GetProperty("public").GetProperty("token").GetString()!;
    }

    private Task<HttpResponseMessage> Open(string token) => Client().GetAsync($"/api/public/dashboards/{token}");

    [Fact]
    public async Task El_enlace_publico_se_mira_sin_login_y_se_renueva_o_se_quita()
    {
        var admin = await As("admin", "admin");
        var id = await NewDashboard(admin);

        // Apagado por defecto: no se puede publicar hasta que el administrador lo habilite.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/dashboards/{id}/public", null)).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/embed/settings")).GetProperty("publicDashboards").GetBoolean());
        (await admin.PutAsJsonAsync("/api/embed/settings", new { publicDashboards = true })).EnsureSuccessStatusCode();

        var token = await Publish(admin, id);
        Assert.True(token.Length >= 40);
        var open = await Open(token);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var body = await open.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ventas", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("seal").GetBoolean()); // Pro lleva el sello
        Assert.DoesNotContain("admin", await open.Content.ReadAsStringAsync()); // ni el dueño ni el enlace
        Assert.Equal(HttpStatusCode.NotFound, (await Client().PostAsJsonAsync($"/api/public/dashboards/{token}/widgets/nada/data", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Open("inventado")).StatusCode);

        // La página pública se deja embeber en cualquier sitio; el resto de /embed, no.
        Assert.Equal("frame-ancestors *", (await Client().GetAsync($"/embed/p/{token}")).Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("frame-ancestors 'self'", (await Client().GetAsync("/embed/dashboards/x")).Headers.GetValues("Content-Security-Policy").Single());

        // Renovar invalida el anterior; quitar lo cierra.
        var renewed = await Publish(admin, id);
        Assert.NotEqual(token, renewed);
        Assert.Equal(HttpStatusCode.NotFound, (await Open(token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Open(renewed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/dashboards/{id}/public")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Open(renewed)).StatusCode);

        // Con el interruptor apagado, ningún enlace responde.
        var again = await Publish(admin, id);
        (await admin.PutAsJsonAsync("/api/embed/settings", new { publicDashboards = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await Open(again)).StatusCode);
        Assert.Equal("frame-ancestors 'self'", (await Client().GetAsync($"/embed/p/{again}")).Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Solo_el_dueno_ve_el_enlace_y_si_se_desactiva_el_enlace_se_cierra()
    {
        var admin = await As("admin", "admin");
        (await admin.PutAsJsonAsync("/api/embed/settings", new { publicDashboards = true })).EnsureSuccessStatusCode();
        await admin.PutAsJsonAsync("/api/security/users/lucas", new { password = "clave-de-lucas" });
        await admin.PutAsJsonAsync("/api/security/users/ana", new { password = "clave-de-ana" });
        (await admin.PutAsJsonAsync("/api/security/profiles/equipo", new
        {
            name = "Equipo", users = new[] { "lucas", "ana" }, dashboards = new[] { "view", "create", "update" }
        })).EnsureSuccessStatusCode();

        var lucas = await As("lucas", "clave-de-lucas");
        var id = await NewDashboard(lucas, "De Lucas");
        (await lucas.PutAsJsonAsync($"/api/dashboards/{id}", new { name = "De Lucas", widgets = Array.Empty<object>(), shares = new[] { new { profile = "equipo" } } }))
            .EnsureSuccessStatusCode();
        var token = await Publish(lucas, id);

        // Ana lo ve compartido y sabe que es público, pero no ve el enlace ni puede publicarlo.
        var ana = await As("ana", "clave-de-ana");
        var seen = await ana.GetFromJsonAsync<JsonElement>($"/api/dashboards/{id}");
        Assert.True(seen.GetProperty("isPublic").GetBoolean());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("dashboard").GetProperty("public").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync($"/api/dashboards/{id}/public", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Open(token)).StatusCode);
        var off = await admin.PutAsJsonAsync("/api/security/users/lucas", new { active = false });
        Assert.True(off.IsSuccessStatusCode, await off.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await Open(token)).StatusCode);
    }

    [Fact]
    public async Task En_Gratis_no_hay_dashboards_publicos()
    {
        var free = await As("admin", "admin", license: "");
        Assert.Equal(HttpStatusCode.Forbidden, (await free.PutAsJsonAsync("/api/embed/settings", new { publicDashboards = true })).StatusCode);
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
