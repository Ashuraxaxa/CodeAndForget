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

public class HealthProbeTests
{
    [Theory]
    [InlineData(null, null, null, "http://localhost:8080/api/health/ready")]
    [InlineData(null, "http://+:8080", "/mantenedores/", "http://localhost:8080/mantenedores/api/health/ready")]
    [InlineData(null, "https://*:443;http://0.0.0.0:5080", "", "http://localhost:5080/api/health/ready")]
    [InlineData("http://otro:9000/salud", "http://+:8080", null, "http://otro:9000/salud")]
    public void Arma_la_url_desde_la_configuracion_del_contenedor(string? url, string? urls, string? pathBase, string expected) =>
        Assert.Equal(expected, Mantenedores.Api.Infrastructure.HealthProbe.Url(url, urls, pathBase));

    [Fact]
    public async Task Falla_si_no_hay_nadie_escuchando() =>
        Assert.Equal(1, await Mantenedores.Api.Infrastructure.HealthProbe.RunAsync("http://127.0.0.1:1/api/health/ready"));

}

/// <summary>Redes de confianza del proxy (Hosting:ForwardedHeaders:KnownNetworks).</summary>
public class ForwardedNetworkTests
{
    [Theory]
    [InlineData("10.0.0.0/8", "10.0.0.0/8")]
    [InlineData("192.168.1.5/24", "192.168.1.0/24")] // con bits de host, como se aceptaba antes de .NET 10
    [InlineData("172.16.5.9/12", "172.16.0.0/12")]
    [InlineData("fd00::1/64", "fd00::/64")]
    public void Las_redes_del_proxy_aceptan_cidr_con_o_sin_bits_de_host(string cidr, string expected) =>
        Assert.Equal(expected, Mantenedores.Api.Infrastructure.HostingSetup.ParseNetwork(cidr).ToString());

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("no-es-ip/8")]
    public void Una_red_mal_escrita_dice_cual(string cidr) =>
        Assert.Contains(cidr, Assert.Throws<FormatException>(() => Mantenedores.Api.Infrastructure.HostingSetup.ParseNetwork(cidr)).Message);
}
