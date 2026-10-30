using System.Security.Claims;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Security;

namespace Mantenedores.Api.Endpoints;

/// <summary>
/// Tokens de acceso del usuario (P-113, D-100), p. ej. para conectar una IA por MCP. Cada uno ve, crea y borra solo
/// los suyos, y el token tiene exactamente su acceso. Con un token no se pueden crear otros (ni listarlos ni borrarlos).
/// </summary>
public static class TokenEndpoints
{
    public sealed record CreateTokenRequest(string Name, int? Days);
    public sealed record TokenInfo(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt);
    public sealed record CreatedToken(TokenInfo Token, string Secret);

    public static void MapTokens(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens").RequireAuthorization();

        // Para la página de tokens: si el servidor MCP está activo (P-114) y en qué ruta, para mostrar cómo conectarse.
        group.MapGet("/mcp", (Microsoft.Extensions.Options.IOptions<Mcp.McpOptions> mcp) =>
            new { enabled = mcp.Value.Enabled, path = Mcp.McpSetup.Path.TrimStart('/') });

        group.MapGet("/", async (ClaimsPrincipal user, IAccessTokenStore store, CancellationToken ct) =>
            (await store.ListAsync(Owner(user), ct)).Select(Info));

        group.MapPost("/", async (CreateTokenRequest body, ClaimsPrincipal user, IAccessTokenStore store, CancellationToken ct) =>
        {
            var owner = Owner(user);
            var name = (body.Name ?? "").Trim();
            if (name.Length is 0 or > 100) throw new ValidationException("name", "es obligatorio (máx. 100)");
            if (body.Days is < 1 or > 3650) throw new ValidationException("days", "entre 1 y 3650 días, o sin vencimiento");
            var secret = AccessToken.NewSecret();
            var token = new AccessToken
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, Owner = owner,
                Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Prefix = secret[..(AccessToken.SecretPrefix.Length + 6)], Hash = AccessToken.HashOf(secret),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = body.Days is { } d ? DateTimeOffset.UtcNow.AddDays(d) : null
            };
            await store.SaveAsync(token, ct);
            // El secreto se muestra una sola vez: solo se guarda su hash.
            return Results.Ok(new CreatedToken(Info(token), secret));
        });

        group.MapDelete("/{id}", async (string id, ClaimsPrincipal user, IAccessTokenStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, Owner(user), ct) ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>El usuario de la sesión; con un token no se administran tokens (uno robado no puede crear otros).</summary>
    private static string Owner(ClaimsPrincipal user)
    {
        if (user.HasClaim(c => c.Type == AccessTokenHandler.TokenClaim))
            throw new OperationNotAllowedException("Los tokens de acceso se administran desde una sesión, no con otro token.");
        return user.Identity?.Name ?? throw new OperationNotAllowedException("La sesión no tiene un nombre de usuario.");
    }

    private static TokenInfo Info(AccessToken t) => new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.ExpiresAt, t.LastUsedAt);
}
