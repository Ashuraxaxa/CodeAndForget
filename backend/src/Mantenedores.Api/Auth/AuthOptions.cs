namespace Mantenedores.Api.Auth;

/// <summary>Modo de autenticación (D-024).</summary>
public enum AuthMode
{
    /// <summary>Usuarios en configuración y token propio firmado con <see cref="AuthOptions.JwtKey"/>.</summary>
    Local,

    /// <summary>Tokens de un emisor externo (Duende IdentityServer, Keycloak, Entra ID, Auth0, emisor propio…).</summary>
    Jwt
}

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Local;

    /// <summary>Roles que pueden administrar (p. ej. configurar mantenedores). Se comparan sin distinguir mayúsculas.</summary>
    public List<string> AdminRoles { get; set; } = ["admin"];

    /// <summary>
    /// Roles que reciben cada permiso (D-053), p. ej. <c>"ConfigureTables": ["analista-datos"]</c>.
    /// Sin configurar: ConfigureTables → mnt-configurador, ManageConnections → mnt-conexiones. Los AdminRoles tienen todos.
    /// </summary>
    public Dictionary<string, List<string>> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ----- Modo Local -----

    /// <summary>Clave para firmar los tokens del modo Local. Mínimo 32 caracteres. No se usa en modo Jwt.</summary>
    public string JwtKey { get; set; } = "";
    public string Issuer { get; set; } = "mantenedores";
    public int TokenMinutes { get; set; } = 480;
    public List<AuthUser> Users { get; set; } = [];

    // ----- Modo Jwt -----

    public JwtSettings Jwt { get; set; } = new();
}

public sealed class AuthUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}

/// <summary>Validación de tokens de un emisor externo. Se configura una de: Authority, MetadataAddress o SigningKey.</summary>
public sealed class JwtSettings
{
    /// <summary>URL del emisor con descubrimiento OIDC (p. ej. https://login.empresa.com/realms/ventas). Las llaves salen de su JWKS.</summary>
    public string Authority { get; set; } = "";

    /// <summary>URL explícita del documento de descubrimiento, si no es Authority + /.well-known/openid-configuration.</summary>
    public string MetadataAddress { get; set; } = "";

    /// <summary>Llave simétrica (HS256) para emisores propios sin OIDC. Mínimo 32 caracteres.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>Audiencia esperada (claim aud). Vacío = no se valida.</summary>
    public string Audience { get; set; } = "";

    /// <summary>Emisor esperado (claim iss). Con Authority se toma del descubrimiento.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>Exigir HTTPS para el descubrimiento. Solo se apaga en desarrollo.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Claim con el nombre del usuario. Vacío = el primero de: preferred_username, name, email, sub.</summary>
    public string NameClaim { get; set; } = "";

    /// <summary>
    /// Claim con los roles; admite rutas con punto para claims anidados (p. ej. "realm_access.roles" en Keycloak)
    /// y varios separados por coma. Vacío = role, roles y groups.
    /// </summary>
    public string RoleClaim { get; set; } = "";

    /// <summary>Scope que debe traer el token (claim scope o scp). Vacío = no se exige.</summary>
    public string RequiredScope { get; set; } = "";

    public int ClockSkewSeconds { get; set; } = 60;

    // ----- Login desde la UI (Authorization Code + PKCE) -----

    /// <summary>Client id público para que la SPA inicie sesión. Vacío = el token lo entrega un gateway/proxy.</summary>
    public string ClientId { get; set; } = "";

    public string Scopes { get; set; } = "openid profile";

    /// <summary>Nombre que muestra el botón de login ("Ingresar con …"). Vacío: "el proveedor de identidad" en el idioma elegido.</summary>
    public string ProviderName { get; set; } = "";
}
