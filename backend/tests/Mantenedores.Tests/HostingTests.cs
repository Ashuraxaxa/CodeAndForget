using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Publicación bajo subruta, index.html con base ajustada y health checks (P-007, P-008).</summary>
public sealed class HostingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mantenedores-tests-").FullName;

    private HttpClient Client(string pathBase = "")
    {
        var web = Directory.CreateDirectory(Path.Combine(_root, "wwwroot")).FullName;
        File.WriteAllText(Path.Combine(web, "index.html"),
            "<!doctype html><html><head><base href=\"/\" /></head><body>app</body></html>");
        File.WriteAllText(Path.Combine(web, "app.js"), "console.log(1)");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting(WebHostDefaults.WebRootKey, web);
            b.UseSetting("Storage:DataDirectory", Path.Combine(_root, "data"));
            b.UseSetting("Hosting:PathBase", pathBase);
        });
        return factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    public async Task Health_checks_responden_sin_autenticacion(string path)
    {
        var res = await Client().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task En_la_raiz_el_index_mantiene_la_base_raiz()
    {
        var html = await Client().GetStringAsync("/c/demo/t/ventas/producto");
        Assert.Contains("<base href=\"/\" />", html);
    }

    [Fact]
    public async Task Bajo_subruta_el_index_lleva_la_base_de_la_subruta()
    {
        var client = Client("/mantenedores/");
        var html = await client.GetStringAsync("/mantenedores/c/demo/t/ventas/producto");
        Assert.Contains("<base href=\"/mantenedores/\" />", html);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mantenedores/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mantenedores/api/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mantenedores/api/connections")).StatusCode);
    }

    [Fact]
    public async Task Una_ruta_de_api_desconocida_es_404_y_no_el_index()
    {
        var res = await Client("/mantenedores").GetAsync("/mantenedores/api/no-existe");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task El_index_no_se_guarda_en_cache()
    {
        var res = await Client().GetAsync("/");
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
