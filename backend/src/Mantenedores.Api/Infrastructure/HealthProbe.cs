namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// <c>dotnet Mantenedores.Api.dll healthcheck [url]</c> (P-074): consulta /api/health/ready de la instancia local y
/// sale con 0 si responde bien. Sirve como HEALTHCHECK del contenedor, que no trae curl ni wget.
/// </summary>
public static class HealthProbe
{
    /// <summary>
    /// URL a consultar: la dada, o la primera de <c>ASPNETCORE_URLS</c> (con "+", "*" o "0.0.0.0" como localhost)
    /// más la subruta de <c>Hosting__PathBase</c>.
    /// </summary>
    public static string Url(string? explicitUrl, string? urls, string? pathBase)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl)) return explicitUrl.Trim();
        var first = (urls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? "http://localhost:8080";
        var host = first.Replace("://+", "://localhost").Replace("://*", "://localhost").Replace("://0.0.0.0", "://localhost")
            .Replace("://[::]", "://localhost").TrimEnd('/');
        var path = new HostingOptions { PathBase = pathBase ?? "" }.NormalizedPathBase;
        return $"{host}{path}/api/health/ready";
    }

    public static async Task<int> RunAsync(string? explicitUrl)
    {
        var url = Url(explicitUrl, Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
            Environment.GetEnvironmentVariable("Hosting__PathBase"));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var res = await http.GetAsync(url);
            Console.WriteLine($"{url} → {(int)res.StatusCode}");
            return res.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{url} → {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
