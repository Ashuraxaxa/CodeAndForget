using System.Security.Claims;
using System.Text.Encodings.Web;
using Mantenedores.Api.Embed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Sesiones embebidas (<c>Authorization: Bearer emb_…</c>, P-135): las que entrega el canje de un token firmado por una
/// aplicación registrada. El usuario se llama <c>app:{aplicación}/{usuario}</c> y no tiene roles: su acceso sale del perfil
/// de la aplicación recortado al token (<see cref="HttpAccessContext"/>).
/// </summary>
public sealed class EmbedSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    EmbedAppService apps) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "EmbedSession";
    public const string AppClaim = "emb_app";
    public const string ProfileClaim = "emb_profile";
    public const string ScopeClaim = "emb_scope";

    public static bool HasToken(HttpRequest r) =>
        r.Headers.Authorization.ToString().StartsWith("Bearer " + EmbedAppService.SessionPrefix, StringComparison.Ordinal);

    public static bool IsEmbedded(ClaimsPrincipal user) => user.HasClaim(c => c.Type == AppClaim);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HasToken(Request)) return AuthenticateResult.NoResult();
        var token = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (await apps.ReadSessionAsync(token, Context.RequestAborted) is not { } s)
            return AuthenticateResult.Fail("La sesión embebida venció o ya no es válida.");
        var identity = new ClaimsIdentity(SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, EmbedAppService.UserName(s.App, s.Payload)));
        identity.AddClaim(new Claim(AppClaim, s.App.Id));
        identity.AddClaim(new Claim(ProfileClaim, s.App.ProfileId));
        identity.AddClaim(new Claim(ScopeClaim, EmbedAppService.SerializePayload(s.Payload)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
