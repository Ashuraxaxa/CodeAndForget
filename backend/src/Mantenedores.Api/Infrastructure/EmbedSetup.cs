using System.Text.RegularExpressions;
using Mantenedores.Api.Licensing;
using Mantenedores.Core.Licensing;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Desde qué sitios se puede embeber Celdo en un iframe (P-134, D-113).</summary>
public sealed class EmbedOptions
{
    public const string Section = "Embed";

    /// <summary>
    /// Orígenes que pueden poner las páginas <c>/embed</c> en un iframe, p. ej. "https://intranet.empresa.com" o
    /// "https://*.empresa.com". Vacío (por defecto) = nadie de afuera.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];
}

/// <summary>
/// Cabeceras contra el clickjacking. Las páginas <c>/embed</c> se dejan embeber por los sitios permitidos, si el plan lo
/// incluye; todo lo demás, solo por la propia app. Sin esto, cualquier página podía meter Celdo en un iframe.
/// </summary>
public static partial class EmbedSetup
{
    public const string EmbedPath = "/embed";

    // Esquema y host, con un comodín opcional al inicio del host y puerto opcional. Sin rutas: frame-ancestors no las usa
    // como se esperaría, y un error de tipeo abriría de más.
    [GeneratedRegex(@"^https?://(\*\.)?[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*(:\d{1,5})?$")]
    private static partial Regex OriginPattern();

    /// <summary>Normaliza y valida los orígenes al arrancar, con un mensaje que dice cuál está mal.</summary>
    public static IReadOnlyList<string> Validate(EmbedOptions options)
    {
        var list = new List<string>();
        foreach (var raw in options.AllowedOrigins)
        {
            var o = (raw ?? "").Trim().TrimEnd('/').ToLowerInvariant();
            if (o.Length == 0) continue;
            if (!OriginPattern().IsMatch(o))
                throw new InvalidOperationException(
                    $"Embed:AllowedOrigins: '{raw}' no es un origen válido. Use esquema y host, p. ej. https://intranet.empresa.com o https://*.empresa.com, sin ruta.");
            if (!list.Contains(o)) list.Add(o);
        }
        return list;
    }

    /// <summary>Valor de <c>frame-ancestors</c> para una ruta: con los sitios permitidos solo bajo /embed y con el plan.</summary>
    public static string FrameAncestors(PathString path, IReadOnlyList<string> origins, bool planAllows, bool publicDashboards = false) =>
        // Un dashboard público (P-137) es de solo lectura y para cualquiera: se deja embeber en cualquier sitio.
        path.StartsWithSegments(PublicPath) && planAllows && publicDashboards ? "frame-ancestors *"
        : path.StartsWithSegments(EmbedPath) && planAllows && origins.Count > 0
            ? "frame-ancestors 'self' " + string.Join(' ', origins)
            : "frame-ancestors 'self'";

    /// <summary>Páginas de los dashboards públicos.</summary>
    public const string PublicPath = "/embed/p";

    /// <summary>Los sitios son los de <c>Embed:AllowedOrigins</c> más los de las aplicaciones registradas en uso (P-135).</summary>
    public static IApplicationBuilder UseFrameProtection(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            IReadOnlyList<string> origins = [];
            var planAllows = false;
            var publicDashboards = false;
            // El plan y los sitios solo se consultan para /embed (los dos están en caché 30 s).
            if (ctx.Request.Path.StartsWithSegments(EmbedPath))
            {
                planAllows = (await ctx.RequestServices.GetRequiredService<LicenseService>().GetAsync(ctx.RequestAborted)).Limits.Has(Feature.Embed);
                var apps = ctx.RequestServices.GetRequiredService<Embed.EmbedAppService>();
                if (planAllows) origins = await apps.AllowedOriginsAsync(ctx.RequestAborted);
                if (planAllows && ctx.Request.Path.StartsWithSegments(PublicPath)) publicDashboards = await apps.PublicDashboardsAsync(ctx.RequestAborted);
            }
            var policy = FrameAncestors(ctx.Request.Path, origins, planAllows, publicDashboards);
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h.ContentSecurityPolicy = policy;
                // X-Frame-Options no admite una lista: solo se envía cuando no se deja embeber desde afuera.
                if (policy == "frame-ancestors 'self'") h.XFrameOptions = "SAMEORIGIN";
                h.XContentTypeOptions = "nosniff";
                return Task.CompletedTask;
            });
            await next();
        });
}
