using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Quién puede poner Celdo en un iframe (P-134, D-113).</summary>
public sealed class EmbedTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("embed-tests-").FullName;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private HttpClient Client(string licenseKey, params string[] origins)
    {
        var web = Directory.CreateDirectory(Path.Combine(_root, "wwwroot")).FullName;
        File.WriteAllText(Path.Combine(web, "index.html"), "<!doctype html><html><head><base href=\"/\" /></head><body>app</body></html>");
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting(WebHostDefaults.WebRootKey, web);
            b.UseSetting("Storage:DataDirectory", Path.Combine(_root, "data"));
            b.UseSetting("License:Key", licenseKey);
            for (var i = 0; i < origins.Length; i++) b.UseSetting($"Embed:AllowedOrigins:{i}", origins[i]);
        });
        _factories.Add(f);
        return f.CreateClient();
    }

    private static async Task<(string? Csp, string? Xfo)> Headers(HttpClient c, string path)
    {
        var res = await c.GetAsync(path);
        return (res.Headers.TryGetValues("Content-Security-Policy", out var csp) ? string.Join(",", csp) : null,
            res.Headers.TryGetValues("X-Frame-Options", out var xfo) ? string.Join(",", xfo) : null);
    }

    [Fact]
    public async Task Sin_sitios_configurados_solo_la_propia_app_puede_embeber()
    {
        var c = Client(TestLicense.Issue(Plan.Pro));
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/"));
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/embed/dashboards/abc"));
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/api/health"));
    }

    [Fact]
    public async Task Con_Pro_los_sitios_permitidos_embeben_solo_las_paginas_embed()
    {
        var c = Client(TestLicense.Issue(Plan.Pro), "https://Intranet.Empresa.com/", "https://*.empresa.com");
        Assert.Equal(("frame-ancestors 'self' https://intranet.empresa.com https://*.empresa.com", null),
            await Headers(c, "/embed/c/demo/t/ventas/producto"));
        // El resto de la app sigue cerrado: un sitio permitido para /embed no puede embeber la administración.
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/seguridad"));
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/embedido"));
    }

    [Fact]
    public async Task En_Gratis_no_se_embebe_aunque_haya_sitios()
    {
        var c = Client("", "https://intranet.empresa.com");
        Assert.Equal(("frame-ancestors 'self'", "SAMEORIGIN"), await Headers(c, "/embed/dashboards/abc"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("intranet.empresa.com")]
    [InlineData("https://intranet.empresa.com/portal")]
    [InlineData("https://*")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://a.com https://b.com")]
    public void Un_origen_invalido_se_rechaza_al_arrancar(string origin)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmbedSetup.Validate(new EmbedOptions { AllowedOrigins = [origin] }));
        Assert.Contains("Embed:AllowedOrigins", ex.Message);
    }

    [Fact]
    public void Los_origenes_se_normalizan_y_sin_repetidos()
    {
        var list = EmbedSetup.Validate(new EmbedOptions { AllowedOrigins = ["https://A.com/", "https://a.com", " ", "http://localhost:3000"] });
        Assert.Equal(["https://a.com", "http://localhost:3000"], list);
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
