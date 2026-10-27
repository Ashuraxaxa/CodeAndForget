using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Configura la validación de tokens según <see cref="AuthOptions.Mode"/> (D-024). En ambos modos, al validar el
/// token se agregan claims normalizados (ClaimTypes.Name y ClaimTypes.Role) según el mapeo configurado,
/// así el resto de la app no conoce el formato de cada emisor.
/// </summary>
public static class AuthSetup
{
    public const string AdminPolicy = "admin";

    /// <summary>Valida la configuración al arrancar, con mensajes que dicen qué falta.</summary>
    public static void Validate(AuthOptions auth)
    {
        if (auth.Permissions.Keys.FirstOrDefault(k => !Permissions.All.Contains(k, StringComparer.OrdinalIgnoreCase)) is { } unknown)
            throw new InvalidOperationException(
                $"Auth:Permissions:{unknown} no existe. Permisos válidos: {string.Join(", ", Permissions.All)}.");
        if (auth.Mode == AuthMode.Local)
        {
            if (auth.JwtKey.Length < 32)
                throw new InvalidOperationException(
                    "Modo Local: configure Auth:JwtKey con al menos 32 caracteres (variable de entorno Auth__JwtKey).");
            return;
        }

        var j = auth.Jwt;
        if (j.Authority.Length == 0 && j.MetadataAddress.Length == 0 && j.SigningKey.Length == 0)
            throw new InvalidOperationException(
                "Modo Jwt: configure Auth:Jwt:Authority (emisor OIDC), Auth:Jwt:MetadataAddress o Auth:Jwt:SigningKey.");
        if (j.SigningKey.Length is > 0 and < 32)
            throw new InvalidOperationException("Modo Jwt: Auth:Jwt:SigningKey debe tener al menos 32 caracteres.");
    }

    public static void AddMantenedoresAuth(this IServiceCollection services, AuthOptions auth)
    {
        Validate(auth);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            // Se conservan los nombres originales de los claims (sin el mapeo de Microsoft) para leerlos tal cual.
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };

            if (auth.Mode == AuthMode.Local)
            {
                o.TokenValidationParameters.ValidIssuer = auth.Issuer;
                o.TokenValidationParameters.ValidAudience = auth.Issuer;
                o.TokenValidationParameters.IssuerSigningKey = TokenService.SigningKey(auth.JwtKey);
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(1);
            }
            else
            {
                var j = auth.Jwt;
                if (j.Authority.Length > 0) o.Authority = j.Authority;
                if (j.MetadataAddress.Length > 0) o.MetadataAddress = j.MetadataAddress;
                o.RequireHttpsMetadata = j.RequireHttpsMetadata;

                var p = o.TokenValidationParameters;
                p.ClockSkew = TimeSpan.FromSeconds(j.ClockSkewSeconds);
                p.ValidateAudience = j.Audience.Length > 0;
                if (j.Audience.Length > 0) p.ValidAudience = j.Audience;
                if (j.Issuer.Length > 0) p.ValidIssuer = j.Issuer;
                // Sin descubrimiento no hay de dónde tomar el emisor: se valida solo si se configuró.
                p.ValidateIssuer = j.Issuer.Length > 0 || j.Authority.Length > 0 || j.MetadataAddress.Length > 0;
                if (j.SigningKey.Length > 0) p.IssuerSigningKey = TokenService.SigningKey(j.SigningKey);
            }

            o.Events = new JwtBearerEvents { OnTokenValidated = ctx => Normalize(ctx, auth) };
        });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AdminPolicy, p => p.RequireAssertion(ctx =>
                ClaimsMapper.IsAdmin(ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value), auth.AdminRoles)));
            foreach (var permission in Permissions.All)
                o.AddPolicy(Permissions.Policy(permission), p => p.RequireAssertion(ctx => Permissions.Has(auth, ctx.User, permission)));
        });
    }

    private static Task Normalize(TokenValidatedContext ctx, AuthOptions auth)
    {
        var principal = ctx.Principal!;
        var (nameClaim, roleClaim) = auth.Mode == AuthMode.Local
            ? ("preferred_username", "role")
            : (auth.Jwt.NameClaim, auth.Jwt.RoleClaim);

        if (auth.Mode == AuthMode.Jwt && auth.Jwt.RequiredScope.Length > 0)
        {
            var scopes = principal.FindAll("scope").Concat(principal.FindAll("scp"))
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!scopes.Contains(auth.Jwt.RequiredScope, StringComparer.Ordinal))
            {
                ctx.Fail($"El token no trae el scope requerido '{auth.Jwt.RequiredScope}'.");
                return Task.CompletedTask;
            }
        }

        var identity = new ClaimsIdentity(principal.Identity, null, principal.Identity?.AuthenticationType,
            ClaimTypes.Name, ClaimTypes.Role);
        if (ClaimsMapper.Name(principal, nameClaim) is { } name)
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
        foreach (var role in ClaimsMapper.Roles(principal, roleClaim))
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        ctx.Principal = new ClaimsPrincipal(identity);
        return Task.CompletedTask;
    }
}
