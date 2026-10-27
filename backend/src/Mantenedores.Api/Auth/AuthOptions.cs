namespace Mantenedores.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>Clave para firmar los JWT. Mínimo 32 caracteres. En producción va por variable de entorno.</summary>
    public string JwtKey { get; set; } = "";
    public string Issuer { get; set; } = "mantenedores";
    public int TokenMinutes { get; set; } = 480;

    /// <summary>Login básico de Fase 1. En Fase 3 se reemplaza por SSO (OIDC / Entra ID).</summary>
    public List<AuthUser> Users { get; set; } = [];
}

public sealed class AuthUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
