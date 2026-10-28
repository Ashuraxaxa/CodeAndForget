using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Apartado de seguridad (P-040, P-042): usuarios propios, perfiles y permiso ManageSecurity (D-062–D-066).</summary>
public sealed class SecurityApiTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("security-tests-").FullName;
    private readonly HttpClient _c;

    public SecurityApiTests()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development"); // admin / admin con rol admin
            b.UseSetting("Storage:DataDirectory", _data);
        });
        _c = factory.CreateClient();
    }

    private async Task<string?> Login(string user, string password)
    {
        var res = await _c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        return res.IsSuccessStatusCode ? (await res.Content.ReadFromJsonAsync<LoginDto>())!.Token : null;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _c.SendAsync(req);
    }

    private async Task<MeDto> Me(string token) =>
        (await (await Send(HttpMethod.Get, "/api/auth/me", token)).Content.ReadFromJsonAsync<MeDto>())!;

    [Fact]
    public async Task Usuarios_propios_inician_sesion_y_los_perfiles_les_dan_permisos()
    {
        var admin = (await Login("admin", "admin"))!;
        Assert.True((await Me(admin)).SecurityOpen);
        Assert.Contains("ManageSecurity", (await Me(admin)).Permissions);

        // Alta de un usuario propio; la contraseña nunca vuelve.
        var put = await Send(HttpMethod.Put, "/api/security/users/Ana", admin, new { displayName = "Ana Pérez", password = "clave-segura-1" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.DoesNotContain("clave", await put.Content.ReadAsStringAsync());

        var ana = await Login("ana", "clave-segura-1");
        Assert.NotNull(ana);
        Assert.Null(await Login("ana", "otra-clave"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/security/profiles", ana!)).StatusCode);

        // Una conexión cualquiera: sin perfiles, ana la ve (seguridad abierta).
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/connections/demo", admin,
            new { name = "Demo", engine = "Postgres", connectionString = "Host=nada;Database=x" })).StatusCode);
        Assert.Single(await (await Send(HttpMethod.Get, "/api/connections", ana!)).Content.ReadFromJsonAsync<List<object>>() ?? []);

        // Con un perfil que no la incluye, deja de ver datos.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/security/profiles/lectores", admin, new
        {
            name = "Lectores", roles = new[] { "lector" }, connections = new Dictionary<string, object> { ["demo"] = new { operations = new[] { "view" } } }
        })).StatusCode);
        Assert.False((await Me(ana!)).SecurityOpen);
        Assert.Empty(await (await Send(HttpMethod.Get, "/api/connections", ana!)).Content.ReadFromJsonAsync<List<object>>() ?? []);

        // Un perfil para ella con ManageSecurity: ahora administra la seguridad y ve la conexión.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/security/profiles/seguridad", admin, new
        {
            name = "Seguridad", users = new[] { "ANA" }, appPermissions = new[] { "managesecurity" },
            connections = new Dictionary<string, object> { ["*"] = new { operations = new[] { "View", "export" } } }
        })).StatusCode);
        var me = await Me(ana!);
        Assert.Equal(["ManageSecurity"], me.Permissions);
        Assert.Equal(["Seguridad"], me.Profiles);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/security/profiles", ana!)).StatusCode);
        Assert.Single(await (await Send(HttpMethod.Get, "/api/connections", ana!)).Content.ReadFromJsonAsync<List<object>>() ?? []);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, "/api/connections/demo", ana!)).StatusCode);
        // Los ajustes nombran tablas y columnas que un perfil puede ocultar: leerlos exige ConfigureTables.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/meta/demo/overrides", ana!)).StatusCode);

        // Al desactivarla, su token deja de valer aunque no haya vencido.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/security/users/ana", admin, new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/auth/me", ana!)).StatusCode);
        Assert.Null(await Login("ana", "clave-segura-1"));

        var users = await (await Send(HttpMethod.Get, "/api/security/users", admin)).Content.ReadFromJsonAsync<List<UserDto>>();
        Assert.Contains(users!, u => u is { Username: "admin", Source: "config" });
        Assert.Contains(users!, u => u is { Username: "ana", Source: "local", Active: false, DisplayName: "Ana Pérez" });

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, "/api/security/profiles/seguridad", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, "/api/security/users/ana", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, "/api/security/users/ana", admin)).StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"X","connections":{"demo":{"operations":["borrar"]}}}""", "connections")]
    [InlineData("""{"name":"X","appPermissions":["BorrarTodo"]}""", "appPermissions")]
    [InlineData("""{"name":"X","connections":{"demo":{"tables":{"sinpunto":{"operations":["view"]}}}}}""", "connections")]
    [InlineData("""{"name":"X","connections":{"demo":{"tables":{"a.b":{"columns":{"c":{"operations":["delete"]}}}}}}}""", "connections")]
    [InlineData("""{"name":"  "}""", "name")]
    public async Task Rechaza_perfiles_mal_formados(string json, string field)
    {
        var admin = (await Login("admin", "admin"))!;
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/security/profiles/p1")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var res = await _c.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains($"\"{field}\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rechaza_usuarios_que_chocan_con_la_configuracion_o_sin_clave_suficiente()
    {
        var admin = (await Login("admin", "admin"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/security/users/admin", admin, new { password = "clave-segura-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/security/users/luis", admin, new { password = "corta" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/security/users/luis", admin, new { displayName = "Sin clave" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/security/users/con espacio", admin, new { password = "clave-segura-1" })).StatusCode);
    }

    [Fact]
    public async Task La_auditoria_exige_ViewAudit()
    {
        var admin = (await Login("admin", "admin"))!;
        var res = await Send(HttpMethod.Get, "/api/audit?conn=demo&k.id=1&page=1", admin);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"total\":0", await res.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/audit?op=leer", admin)).StatusCode);

        await Send(HttpMethod.Put, "/api/security/users/luis", admin, new { password = "clave-segura-1" });
        var luis = (await Login("luis", "clave-segura-1"))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/audit", luis)).StatusCode);
    }

    private sealed record MeDto(string? Name, List<string> Permissions, bool SecurityOpen, List<string> Profiles);
    private sealed record UserDto(string Username, string? DisplayName, bool Active, string Source);
    private sealed record LoginDto(string Token);

    public void Dispose()
    {
        _c.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
