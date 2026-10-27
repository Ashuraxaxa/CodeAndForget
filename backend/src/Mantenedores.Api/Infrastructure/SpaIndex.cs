using Microsoft.Extensions.FileProviders;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Sirve index.html con la etiqueta &lt;base href&gt; ajustada a la subruta real.
/// Así el mismo build del frontend funciona en "/" o en "/mantenedores" sin recompilar (P-007):
/// los recursos y las llamadas a la API son relativos a esa base.
/// </summary>
public static class SpaIndex
{
    private const string DefaultBase = "<base href=\"/\" />";

    public static void MapSpaIndex(this WebApplication app)
    {
        var file = app.Environment.WebRootFileProvider.GetFileInfo("index.html");

        app.MapFallback(async ctx =>
        {
            if (!file.Exists)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsync("Frontend no compilado: copie frontend/dist en wwwroot.");
                return;
            }

            var html = await ReadAsync(file);
            var baseHref = ctx.Request.PathBase.HasValue ? ctx.Request.PathBase.Value!.TrimEnd('/') + "/" : "/";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            await ctx.Response.WriteAsync(html.Replace(DefaultBase, $"<base href=\"{System.Net.WebUtility.HtmlEncode(baseHref)}\" />"));
        }).AllowAnonymous();
    }

    private static async Task<string> ReadAsync(IFileInfo file)
    {
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
