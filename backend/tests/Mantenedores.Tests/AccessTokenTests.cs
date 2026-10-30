using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Tokens de acceso y propuestas de ajustes para la IA (P-113, D-100), con la metadata por defecto (SQLite).</summary>
public sealed class AccessTokenTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("tokens-tests-").FullName;
    private WebApplicationFactory<Program>? _factory;

    private HttpClient Client()
    {
        _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            b.UseSetting("RateLimit:Enabled", "false");
        });
        return _factory.CreateClient();
    }

    private static async Task<HttpClient> As(HttpClient c, string user, string password)
    {
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        login.EnsureSuccessStatusCode();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private HttpClient WithToken(string secret)
    {
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return c;
    }

    private static async Task<string> CreateAsync(HttpClient c, string name, int? days = null)
    {
        var res = await c.PostAsJsonAsync("/api/tokens", new { name, days });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var secret = body.GetProperty("secret").GetString()!;
        Assert.StartsWith("mnt_", secret);
        Assert.StartsWith(body.GetProperty("token").GetProperty("prefix").GetString()!, secret);
        return secret;
    }

    [Fact]
    public async Task El_token_actua_como_quien_lo_creo_y_no_crea_otros()
    {
        var admin = await As(Client(), "admin", "admin");
        var secret = await CreateAsync(admin, "Claude", days: 30);

        var ia = WithToken(secret);
        var me = await ia.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("admin", me.GetProperty("name").GetString());
        Assert.True(me.GetProperty("isAdmin").GetBoolean()); // el mismo acceso que su dueño
        Assert.Equal(HttpStatusCode.OK, (await ia.GetAsync("/api/connections")).StatusCode);

        // Con un token no se administran tokens.
        Assert.Equal(HttpStatusCode.BadRequest, (await ia.PostAsJsonAsync("/api/tokens", new { name = "otro" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ia.GetAsync("/api/tokens")).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/tokens");
        var token = Assert.Single(list.EnumerateArray());
        Assert.Equal("Claude", token.GetProperty("name").GetString());
        Assert.False(token.TryGetProperty("hash", out _)); // el hash no sale
        Assert.NotEqual(JsonValueKind.Null, token.GetProperty("lastUsedAt").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/tokens/{token.GetProperty("id").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ia.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken("mnt_inventado").GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Un_usuario_propio_desactivado_deja_su_token_sin_acceso()
    {
        var admin = await As(Client(), "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/security/users/rosa", new { password = "clave-de-rosa" })).StatusCode);
        var rosa = await As(Client(), "rosa", "clave-de-rosa");
        var secret = await CreateAsync(rosa, "IA de Rosa");
        var ia = WithToken(secret);
        var me = await ia.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("rosa", me.GetProperty("name").GetString());
        Assert.False(me.GetProperty("isAdmin").GetBoolean());

        // Rosa no ve los tokens de admin ni puede borrar los ajenos.
        Assert.Empty((await rosa.GetFromJsonAsync<JsonElement>("/api/tokens")).EnumerateArray().Where(t => t.GetProperty("name").GetString() != "IA de Rosa"));

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/security/users/rosa", new { active = false })).StatusCode);
        await Task.Delay(100);
        for (var i = 0; i < 120 && (await ia.GetAsync("/api/auth/me")).StatusCode != HttpStatusCode.Unauthorized; i++) await Task.Delay(100);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ia.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task La_conexion_guarda_si_permite_la_muestra_y_sin_permiso_se_rechaza()
    {
        var admin = await As(Client(), "admin", "admin");
        var put = await admin.PutAsJsonAsync("/api/connections/ventas", new
        {
            id = "ventas", name = "Ventas", engine = "Postgres", connectionString = "Host=nadie;Database=x", allowSamples = true
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.True((await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowSamples").GetBoolean());
        await admin.PutAsJsonAsync("/api/connections/ventas", new { id = "ventas", name = "Ventas", engine = "Postgres" });
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/connections");
        Assert.False(list[0].GetProperty("allowSamples").GetBoolean()); // al editar sin marcarla, queda apagada
    }

    [Fact]
    public async Task Sin_permiso_de_configurar_no_se_proponen_ajustes()
    {
        var admin = await As(Client(), "admin", "admin");
        await admin.PutAsJsonAsync("/api/security/users/lucas", new { password = "clave-de-lucas" });
        // Con un perfil (la seguridad deja de estar abierta), Lucas no tiene ConfigureTables.
        await admin.PutAsJsonAsync("/api/security/profiles/lectura", new { name = "Lectura", users = new[] { "lucas" }, rules = Array.Empty<object>() });
        var lucas = WithToken(await CreateAsync(await As(Client(), "lucas", "clave-de-lucas"), "IA"));
        var res = await lucas.PostAsJsonAsync("/api/meta/ventas/proposals", new { schema = "ventas", table = "producto", value = new { label = "x" } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
