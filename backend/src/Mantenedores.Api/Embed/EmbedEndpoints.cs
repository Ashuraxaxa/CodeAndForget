using System.Security.Claims;
using System.Text.RegularExpressions;
using Mantenedores.Api.Auth;

namespace Mantenedores.Api.Embed;

/// <summary>
/// Embeber con tokens firmados (P-135, D-113):
/// <list type="bullet">
/// <item><c>/api/embed/apps</c>: las aplicaciones registradas (permiso <c>ManageSecurity</c>).</item>
/// <item><c>POST /api/embed/session</c>: canjea el JWT de la app anfitriona por una sesión embebida (sin login).</item>
/// </list>
/// </summary>
public static partial class EmbedEndpoints
{
    public sealed record SessionRequest(string? Token);
    public sealed record CreatedApp(EmbedAppView App, string Secret);
    public sealed record RotatedSecret(string Secret);

    public static void MapEmbed(this IEndpointRouteBuilder app)
    {
        var manage = Permissions.Policy(Permissions.ManageSecurity);
        var group = app.MapGroup("/api/embed/apps").RequireAuthorization(manage);
        group.MapGet("/", (EmbedAppService apps, CancellationToken ct) => apps.ListAsync(ct));
        group.MapPost("/", async (EmbedAppInput body, ClaimsPrincipal user, EmbedAppService apps, CancellationToken ct) =>
        {
            var (view, secret) = await apps.CreateAsync(body, user.Identity?.Name ?? "", ct);
            return Results.Ok(new CreatedApp(view, secret));
        });
        group.MapPut("/{id}", (string id, EmbedAppInput body, EmbedAppService apps, CancellationToken ct) => apps.UpdateAsync(id, body, ct));
        group.MapPost("/{id}/secret", async (string id, EmbedAppService apps, CancellationToken ct) =>
            new RotatedSecret(await apps.RotateSecretAsync(id, ct)));
        group.MapDelete("/{id}", async (string id, EmbedAppService apps, CancellationToken ct) =>
        {
            await apps.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        app.MapPost("/api/embed/session", async (SessionRequest body, EmbedAppService apps, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Token))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Falta el token firmado.");
            try { return Results.Ok(await apps.ExchangeAsync(body.Token.Trim(), ct)); }
            catch (EmbedTokenException e) { return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: e.Message); }
        }).AllowAnonymous().RequireRateLimiting(Infrastructure.RateLimiting.Login);
    }

    // Lo único que puede pedir una sesión embebida: ver quién es y el plan, leer la definición de las tablas, trabajar con
    // los datos y mirar un dashboard. Todo lo demás (administrar, auditar, tokens, OData, MCP…) responde 403.
    [GeneratedRegex(@"^/api/(auth/(me|config)|license|meta/[^/]+/tables(/[^/]+/[^/]+)?|dashboards/[^/]+)$")]
    private static partial Regex ReadOnlyPaths();
    [GeneratedRegex(@"^/api/(data/.+|dashboards/[^/]+/widgets/[^/]+/data|embed/session|health(/.*)?)$")]
    private static partial Regex AnyMethodPaths();

    public static bool Allowed(string method, string path) =>
        AnyMethodPaths().IsMatch(path) || (HttpMethods.IsGet(method) && ReadOnlyPaths().IsMatch(path) && path != "/api/dashboards/profiles");

    /// <summary>Corta lo que una sesión embebida no puede pedir, antes de llegar a los endpoints.</summary>
    public static IApplicationBuilder UseEmbedSessionGate(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (EmbedSessionHandler.IsEmbedded(ctx.User) && !Allowed(ctx.Request.Method, ctx.Request.Path.Value ?? ""))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { type = "mantenedores:forbidden", title = "No disponible en una página embebida.", status = 403 });
            return;
        }
        await next();
    });
}
