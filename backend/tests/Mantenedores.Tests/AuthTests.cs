using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Mantenedores.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Tests;

/// <summary>Modos de autenticación (P-041, D-024): Local y emisor JWT externo, con un emisor de prueba en memoria.</summary>
public sealed class AuthTests : IDisposable
{
    private const string Key = "clave-del-emisor-de-prueba-0123456789abcdef";
    private const string Issuer = "https://emisor.prueba";
    private const string Audience = "mantenedores-api";
    private readonly string _data = Directory.CreateTempSubdirectory("auth-tests-").FullName;

    private HttpClient Client(params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });
        return factory.CreateClient();
    }

    private HttpClient JwtClient(params (string, string)[] extra) => Client(
        [("Auth:Mode", "Jwt"), ("Auth:JwtKey", ""), ("Auth:Jwt:SigningKey", Key), ("Auth:Jwt:Issuer", Issuer),
         ("Auth:Jwt:Audience", Audience), .. extra]);

    /// <summary>Token como el de Keycloak: roles anidados en realm_access.roles.</summary>
    private static string Token(Dictionary<string, object> claims, string audience = Audience, DateTime? expires = null)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)
        });
    }

    private static Dictionary<string, object> KeycloakLike(params string[] roles) => new()
    {
        ["sub"] = "1234",
        ["preferred_username"] = "ana",
        ["scope"] = "openid profile mantenedores",
        ["realm_access"] = new Dictionary<string, object> { ["roles"] = roles }
    };

    private static async Task<HttpResponseMessage> Get(HttpClient c, string path, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task Modo_jwt_acepta_tokens_del_emisor_y_mapea_roles_anidados()
    {
        var c = JwtClient(("Auth:Jwt:RoleClaim", "realm_access.roles"));
        var res = await Get(c, "/api/auth/me", Token(KeycloakLike("admin", "ventas")));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var me = await res.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("ana", me!.Name);
        Assert.Equal(["admin", "ventas"], me.Roles);
        Assert.True(me.IsAdmin);

        Assert.Equal(HttpStatusCode.OK, (await Get(c, "/api/connections", Token(KeycloakLike("ventas")))).StatusCode);
    }

    [Fact]
    public async Task Modo_jwt_rechaza_audiencia_ajena_vencido_firma_ajena_y_sin_token()
    {
        var c = JwtClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(c, "/api/connections", Token(KeycloakLike(), audience: "otra"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(c, "/api/connections",
            Token(KeycloakLike(), expires: DateTime.UtcNow.AddMinutes(-10)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(c, "/api/connections", null)).StatusCode);

        var otherKey = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Claims = KeycloakLike(),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("otra-clave-distinta-0123456789abcdefghij")), SecurityAlgorithms.HmacSha256)
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(c, "/api/connections", otherKey)).StatusCode);
    }

    [Fact]
    public async Task Modo_jwt_exige_el_scope_configurado()
    {
        var c = JwtClient(("Auth:Jwt:RequiredScope", "mantenedores"));
        Assert.Equal(HttpStatusCode.OK, (await Get(c, "/api/auth/me", Token(KeycloakLike()))).StatusCode);

        var sinScope = KeycloakLike();
        sinScope["scope"] = "openid profile";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(c, "/api/auth/me", Token(sinScope))).StatusCode);
    }

    [Fact]
    public async Task Modo_jwt_sin_rol_admin_no_es_admin_y_roles_por_defecto()
    {
        // Sin RoleClaim se leen role, roles y groups (formato de Entra ID, Duende y otros).
        var c = JwtClient();
        var me = await (await Get(c, "/api/auth/me", Token(new Dictionary<string, object>
        {
            ["sub"] = "9", ["name"] = "Luis", ["roles"] = new[] { "lectura" }, ["groups"] = new[] { "ventas" }
        }))).Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("Luis", me!.Name);
        Assert.Equal(["lectura", "ventas"], me.Roles);
        Assert.False(me.IsAdmin);
    }

    [Fact]
    public async Task Modo_jwt_publica_su_configuracion_sin_secretos_y_desactiva_el_login_local()
    {
        var c = JwtClient(("Auth:Jwt:Authority", ""), ("Auth:Jwt:ClientId", "mantenedores-spa"),
            ("Auth:Jwt:ProviderName", "Keycloak"));
        var json = await c.GetStringAsync("/api/auth/config");
        Assert.Contains("\"mode\":\"Jwt\"", json);
        Assert.Contains("\"clientId\":\"mantenedores-spa\"", json);
        Assert.DoesNotContain(Key, json);

        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
    }

    [Fact]
    public async Task Modo_jwt_arranca_sin_la_clave_del_modo_local()
    {
        // P-039: en modo Jwt no se exige Auth:JwtKey.
        var res = await JwtClient().GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Modo_local_emite_token_con_los_roles_configurados()
    {
        var c = Client();
        Assert.Contains("\"mode\":\"Local\"", await c.GetStringAsync("/api/auth/config"));

        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        var token = (await login.Content.ReadFromJsonAsync<LoginDto>())!.Token;
        var me = await (await Get(c, "/api/auth/me", token)).Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("admin", me!.Name);
        Assert.True(me.IsAdmin);

        var bad = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "mala" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Theory]
    [InlineData(AuthMode.Local, "", "", "Auth:JwtKey")]
    [InlineData(AuthMode.Jwt, "", "", "Auth:Jwt:Authority")]
    [InlineData(AuthMode.Jwt, "", "corta", "al menos 32")]
    public void La_configuracion_invalida_explica_que_falta(AuthMode mode, string jwtKey, string signingKey, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AuthSetup.Validate(new AuthOptions
        {
            Mode = mode, JwtKey = jwtKey, Jwt = new JwtSettings { SigningKey = signingKey }
        }));
        Assert.Contains(expected, ex.Message);
    }

    private sealed record MeDto(string? Name, List<string> Roles, bool IsAdmin);
    private sealed record LoginDto(string Token);

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
