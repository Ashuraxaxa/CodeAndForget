using System.Security.Claims;
using Mantenedores.Api.Auth;
using Mantenedores.Api.Licensing;
using Mantenedores.Core.Dashboards;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Query;
using Mantenedores.Core.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Embed;

/// <summary>
/// Dashboards públicos (P-137, D-116): cualquiera con el enlace mira el dashboard, sin login.
/// <list type="bullet">
/// <item>El administrador los habilita en Seguridad → Aplicaciones (apagado por defecto); el dueño crea el enlace.</item>
/// <item>Las tarjetas corren con los permisos del dueño. Quien mira solo elige el período: no exporta, no edita, no abre
/// la grilla.</item>
/// <item>Cada tarjeta se guarda 60 s por enlace, período y tarjeta, para que el tráfico anónimo no llegue a la base.</item>
/// </list>
/// </summary>
public static class PublicDashboardEndpoints
{
    public const string SettingName = "embed.public-dashboards";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public sealed record EmbedSettings(bool PublicDashboards);
    public sealed record PublicDashboard(string Name, string? Description, IReadOnlyList<DashboardWidget> Widgets, bool Seal);
    public sealed record DataRequest(string? From, string? To);

    public static void MapPublicDashboards(this IEndpointRouteBuilder app)
    {
        // Si los dashboards públicos están habilitados: lo ve cualquiera con sesión (para mostrar "Enlace público").
        app.MapGet("/api/embed/settings", async (EmbedAppService apps, CancellationToken ct) =>
            new EmbedSettings(await apps.PublicDashboardsAsync(ct))).RequireAuthorization();
        app.MapPut("/api/embed/settings", async (EmbedSettings body, EmbedAppService apps, CancellationToken ct) =>
        {
            await apps.SetPublicDashboardsAsync(body.PublicDashboards, ct);
            return new EmbedSettings(body.PublicDashboards);
        }).RequireAuthorization(Permissions.Policy(Permissions.ManageSecurity));

        // Crear, renovar o quitar el enlace de un dashboard: el dueño o un administrador.
        app.MapPost("/api/dashboards/{id}/public", async (string id, ClaimsPrincipal user, DashboardService dashboards,
            EmbedAppService apps, LicenseService license, CancellationToken ct) =>
        {
            await license.RequireAsync(Feature.Embed, ct);
            if (!await apps.PublicDashboardsAsync(ct))
                throw new OperationNotAllowedException("Los dashboards públicos están apagados: un administrador los habilita en Seguridad → Aplicaciones.");
            return await dashboards.SetPublicAsync(id, true, Permissions.Roles(user), ct);
        }).RequireAuthorization();
        app.MapDelete("/api/dashboards/{id}/public", (string id, DashboardService dashboards, CancellationToken ct) =>
            dashboards.SetPublicAsync(id, false, [], ct)).RequireAuthorization();

        var open = app.MapGroup("/api/public/dashboards").AllowAnonymous();
        open.MapGet("/{token}", async (string token, HttpContext http, CancellationToken ct) =>
        {
            var (d, plan) = await OpenAsync(http, token, ct);
            return new PublicDashboard(d.Name, d.Description, d.Widgets, plan == Plan.Pro);
        });
        open.MapPost("/{token}/widgets/{widget}/data", async (string token, string widget, DataRequest? body, HttpContext http,
            DashboardService dashboards, IMemoryCache cache, CancellationToken ct) =>
        {
            var (d, _) = await OpenAsync(http, token, ct);
            var key = ("public-widget", token, widget, body?.From, body?.To, d.UpdatedAt);
            if (cache.TryGetValue(key, out AggregateResult? cached) && cached is not null) return cached;
            var result = await dashboards.PublicWidgetDataAsync(d, widget, body?.From, body?.To, ct);
            cache.Set(key, result, CacheFor);
            return result;
        });
    }

    /// <summary>
    /// El dashboard del enlace, con el acceso de su dueño fijado en la solicitud. Cualquier motivo para no mostrarlo (plan,
    /// interruptor apagado, enlace quitado, dueño desactivado) responde lo mismo: no existe.
    /// </summary>
    private static async Task<(Dashboard Dashboard, Plan Plan)> OpenAsync(HttpContext http, string token, CancellationToken ct)
    {
        var sp = http.RequestServices;
        var plan = (await sp.GetRequiredService<LicenseService>().GetAsync(ct));
        var notFound = new NotFoundException("Este enlace no existe o ya no está publicado.");
        if (!plan.Limits.Has(Feature.Embed) || !await sp.GetRequiredService<EmbedAppService>().PublicDashboardsAsync(ct)) throw notFound;
        var d = await sp.GetRequiredService<DashboardService>().FindPublicAsync(token, ct) ?? throw notFound;

        var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
        IReadOnlyList<string> roles = d.Public!.Roles;
        if (auth.Mode == AuthMode.Local)
        {
            var users = sp.GetRequiredService<TokenService>();
            if (!await users.IsActiveAsync(d.Owner, ct)) throw notFound;
            roles = users.RolesOf(d.Owner);
        }
        var access = await sp.GetRequiredService<SecurityService>().AccessForAsync(d.Owner, roles,
            ClaimsMapper.IsAdmin(roles, auth.AdminRoles), ct);
        HttpAccessContext.Use(http, access);
        return (d, plan.Plan);
    }
}
