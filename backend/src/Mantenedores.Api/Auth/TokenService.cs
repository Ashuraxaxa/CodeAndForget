using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Api.Auth;

public sealed class TokenService(IOptions<AuthOptions> options)
{
    public static SymmetricSecurityKey SigningKey(AuthOptions o) => new(Encoding.UTF8.GetBytes(o.JwtKey));

    public (string Token, DateTime ExpiresAt) Issue(string username)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.TokenMinutes);
        var token = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Issuer,
            claims: [new Claim(ClaimTypes.Name, username), new Claim(JwtRegisteredClaimNames.Sub, username)],
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public bool Validate(string username, string password)
    {
        var user = options.Value.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        // Se verifica igual contra un hash ficticio para no revelar qué usuarios existen por tiempo de respuesta.
        return PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash) && user is not null;
    }

    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());
}
