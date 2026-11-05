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

    public static bool HasToken(HttpRequest r) => Secret(r) is not null;

    /// <summary>
    /// El secreto del token: <c>Bearer mnt_…</c>, o la contraseña de <c>Basic</c> (usuario cualquiera) para Power BI y
    /// Excel, que en una fuente OData solo saben enviar usuario y contraseña (P-132).
    /// </summary>
    public static string? Secret(HttpRequest r)
    {
        var h = r.Headers.Authorization.ToString();
        if (h.StartsWith("Bearer " + AccessToken.SecretPrefix, StringComparison.Ordinal)) return h["Bearer ".Length..].Trim();
        if (!h.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var pair = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(h["Basic ".Length..].Trim()));
            var password = pair[(pair.IndexOf(':') + 1)..];
            return password.StartsWith(AccessToken.SecretPrefix, StringComparison.Ordinal) ? password : null;
        }
        catch (FormatException) { return null; }
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Secret(Request) is not { } secret) return AuthenticateResult.NoResult();
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
