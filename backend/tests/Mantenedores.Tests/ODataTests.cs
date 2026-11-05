using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Feed OData para Power BI (P-132): token de acceso como contraseña de Básica y opciones no soportadas.</summary>
public sealed class ODataTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("odata-tests-").FullName;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private HttpClient Client()
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            b.UseSetting("RateLimit:Enabled", "false");
        });
        _factories.Add(f);
        return f.CreateClient();
    }

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    [Fact]
    public async Task Power_bi_entra_con_el_token_como_contrasena()
    {
        var c = Client();
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        var jwt = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/tokens") { Content = JsonContent.Create(new { name = "Power BI", days = 30 }) };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var secret = (await (await c.SendAsync(create)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        // Sin credenciales: 401 que pide usuario y contraseña, para que Power BI y Excel los ofrezcan.
        var anon = await c.GetAsync("/odata/demo/");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.StartsWith("Basic", anon.Headers.WwwAuthenticate.ToString());

        async Task<HttpResponseMessage> Get(string url, AuthenticationHeaderValue auth)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, url);
            r.Headers.Authorization = auth;
            return await c.SendAsync(r);
        }

        // Con el token como contraseña (el usuario da igual) entra: la conexión no existe, pero ya no es 401.
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/odata/no-existe/", Basic("cualquiera", secret))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/odata/no-existe/", Basic("admin", "admin"))).StatusCode); // la contraseña de la app no sirve
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/odata/no-existe/", Basic("x", "mnt_inventado"))).StatusCode);

        // $filter no se ignora: responde 501 para que Power BI filtre en su lado, nunca datos sin filtrar.
        var filter = await Get("/odata/no-existe/tabla?$filter=precio%20gt%201", Basic("x", secret));
        Assert.Equal(HttpStatusCode.NotImplemented, filter.StatusCode);
        Assert.Contains("$filter", await filter.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
