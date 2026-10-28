using System.Security.Claims;
using Mantenedores.Api.Auth;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(string Token, DateTime ExpiresAt, string Username);

    /// <summary>Lo que la UI necesita para iniciar sesión. Nunca incluye secretos (D-024).</summary>
    public sealed record AuthConfigResponse(
        AuthMode Mode, string? Authority, string? ClientId, string? Scopes, string? ProviderName);

    /// <summary>
    /// <c>Permissions</c>: lo que puede hacer el usuario (D-053), para que la UI muestre u oculte acciones.
    /// <c>SecurityOpen</c>: no hay perfiles, así que cualquiera ve y edita todos los datos (D-062).
    /// </summary>
    public sealed record MeResponse(string? Name, IReadOnlyList<string> Roles, bool IsAdmin, IReadOnlyList<string> Permissions,
        bool SecurityOpen, IReadOnlyList<string> Profiles);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/config", (IOptions<AuthOptions> options) =>
        {
            var o = options.Value;
            return o.Mode == AuthMode.Local
                ? new AuthConfigResponse(AuthMode.Local, null, null, null, null)
                : new AuthConfigResponse(AuthMode.Jwt,
                    o.Jwt.Authority.Length > 0 ? o.Jwt.Authority : null,
                    o.Jwt.ClientId.Length > 0 ? o.Jwt.ClientId : null,
                    o.Jwt.Scopes, o.Jwt.ProviderName);
        }).AllowAnonymous();

        app.MapPost("/api/auth/login", async (LoginRequest req, TokenService tokens, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            if (options.Value.Mode != AuthMode.Local)
                return Results.Problem("El inicio de sesión lo hace el proveedor de identidad configurado.",
                    statusCode: 404, title: "Login local desactivado");

            var user = string.IsNullOrWhiteSpace(req.Username) ? null : await tokens.ValidateAsync(req.Username, req.Password ?? "", ct);
            if (user is null)
                return Results.Problem("Usuario o contraseña incorrectos.", statusCode: 401, title: "No autorizado");

            var (token, expires) = tokens.Issue(user);
            return Results.Ok(new LoginResponse(token, expires, user.Username));
        }).AllowAnonymous().RequireRateLimiting(Infrastructure.RateLimiting.Login);

        // Quién soy: nombre y roles ya normalizados desde los claims del emisor.
        app.MapGet("/api/auth/me", async (ClaimsPrincipal user, HttpContext http, HttpAccessContext access, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            var roles = Permissions.Roles(user);
            var mine = await access.ForAsync(user, http, ct);
            return new MeResponse(user.Identity?.Name, roles, ClaimsMapper.IsAdmin(roles, options.Value.AdminRoles),
                await access.PermissionsAsync(user, http, ct), mine.IsOpen, mine.ProfileNames);
        }).RequireAuthorization();
    }
}
