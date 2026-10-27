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

    public sealed record MeResponse(string? Name, IReadOnlyList<string> Roles, bool IsAdmin);

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

        app.MapPost("/api/auth/login", (LoginRequest req, TokenService tokens, IOptions<AuthOptions> options) =>
        {
            if (options.Value.Mode != AuthMode.Local)
                return Results.Problem("El inicio de sesión lo hace el proveedor de identidad configurado.",
                    statusCode: 404, title: "Login local desactivado");

            var user = string.IsNullOrWhiteSpace(req.Username) ? null : tokens.Validate(req.Username, req.Password ?? "");
            if (user is null)
                return Results.Problem("Usuario o contraseña incorrectos.", statusCode: 401, title: "No autorizado");

            var (token, expires) = tokens.Issue(user);
            return Results.Ok(new LoginResponse(token, expires, user.Username));
        }).AllowAnonymous();

        // Quién soy: nombre y roles ya normalizados desde los claims del emisor.
        app.MapGet("/api/auth/me", (ClaimsPrincipal user, IOptions<AuthOptions> options) =>
        {
            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            return new MeResponse(user.Identity?.Name, roles, ClaimsMapper.IsAdmin(roles, options.Value.AdminRoles));
        }).RequireAuthorization();
    }
}
