using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Mantenedores.Core.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Emisión y validación de credenciales del modo Local: usuarios de la configuración (<c>Auth:Users</c>) y usuarios
/// propios guardados en la metadata (D-062). Si un nombre existe en ambos, manda la configuración.
/// </summary>
public sealed class TokenService(IOptions<AuthOptions> options, ISecurityStore store)
{
    public static SymmetricSecurityKey SigningKey(string key) => new(Encoding.UTF8.GetBytes(key));

    public (string Token, DateTime ExpiresAt) Issue(AuthUser user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.TokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new("preferred_username", user.Username)
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r)));

        var token = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Issuer,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(o.JwtKey), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>Devuelve el usuario si la contraseña es correcta y está activo.</summary>
    public async Task<AuthUser?> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var name = username.Trim();
        if (ConfigUser(name) is { } configured)
            return PasswordHasher.Verify(password, configured.PasswordHash) ? configured : null;

        var local = await store.GetUserAsync(name, ct);
        // Se verifica igual contra un hash ficticio para no revelar qué usuarios existen por tiempo de respuesta.
        var ok = PasswordHasher.Verify(password, local?.PasswordHash ?? DummyHash);
        return ok && local is { Active: true } ? new AuthUser { Username = local.Username } : null;
    }

    /// <summary>¿El usuario sigue habilitado? Los de la configuración siempre; los propios, si existen y están activos.</summary>
    public async Task<bool> IsActiveAsync(string username, CancellationToken ct) =>
        ConfigUser(username) is not null || await store.GetUserAsync(username, ct) is { Active: true };

    public bool IsConfigUser(string username) => ConfigUser(username) is not null;

    private AuthUser? ConfigUser(string username) => options.Value.Users.FirstOrDefault(u =>
        string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());
}
