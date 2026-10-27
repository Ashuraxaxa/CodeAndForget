using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Api.Auth;

/// <summary>Emisión y validación de credenciales del modo Local.</summary>
public sealed class TokenService(IOptions<AuthOptions> options)
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

    /// <summary>Devuelve el usuario si la contraseña es correcta.</summary>
    public AuthUser? Validate(string username, string password)
    {
        var user = options.Value.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        // Se verifica igual contra un hash ficticio para no revelar qué usuarios existen por tiempo de respuesta.
        var ok = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash);
        return ok ? user : null;
    }

    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());
}
