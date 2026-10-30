using System.Security.Claims;
using System.Text.Encodings.Web;
using Mantenedores.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Autenticación con tokens de acceso (<c>Authorization: Bearer mnt_…</c>, P-113, D-100). El token actúa como su dueño
/// con lo que ese usuario puede hacer en este momento: en modo Local, los roles actuales de la configuración y que siga
/// activo; en modo Jwt, los roles que tenía al crearlo (el proveedor de identidad no se puede consultar después). Los
/// perfiles se aplican por usuario, igual que en una sesión.
/// </summary>
public sealed class AccessTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IAccessTokenStore tokens, TokenService users, IOptions<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AccessToken";
    /// <summary>Claim con el id del token: las rutas de tokens no aceptan una solicitud hecha con un token.</summary>
    public const string TokenClaim = "mnt_token";

    public static bool HasToken(HttpRequest r) =>
        r.Headers.Authorization.ToString() is { } h && h.StartsWith("Bearer " + AccessToken.SecretPrefix, StringComparison.Ordinal);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HasToken(Request)) return AuthenticateResult.NoResult();
        var secret = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var token = await tokens.FindByHashAsync(AccessToken.HashOf(secret), Context.RequestAborted);
        if (token is null) return AuthenticateResult.Fail("Token de acceso inválido.");
        var now = DateTimeOffset.UtcNow;
        if (token.ExpiresAt is { } exp && exp <= now) return AuthenticateResult.Fail("El token de acceso venció.");

        IEnumerable<string> roles;
        if (auth.Value.Mode == AuthMode.Local)
        {
            if (!await users.IsActiveAsync(token.Owner, Context.RequestAborted)) return AuthenticateResult.Fail("El usuario del token está desactivado.");
            roles = users.RolesOf(token.Owner);
        }
        else roles = token.Roles;

        var identity = new ClaimsIdentity(SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, token.Owner));
        identity.AddClaim(new Claim(TokenClaim, token.Id));
        foreach (var r in roles) identity.AddClaim(new Claim(ClaimTypes.Role, r));
        // La última vez que se usó, sin escribir en cada solicitud.
        if (token.LastUsedAt is null || now - token.LastUsedAt > TimeSpan.FromMinutes(1))
            await tokens.TouchAsync(token.Id, now, CancellationToken.None);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
