namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Cómo se publica la app (D-022): bajo qué subruta y detrás de qué proxies.
/// Por defecto: en la raíz y sin confiar en encabezados de proxy.
/// </summary>
public sealed class HostingOptions
{
    public const string Section = "Hosting";

    /// <summary>Subruta donde se publica, p. ej. "/mantenedores". Vacío = raíz.</summary>
    public string PathBase { get; set; } = "";

    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();

    /// <summary>PathBase normalizado: "" o "/algo" sin barra final.</summary>
    public string NormalizedPathBase
    {
        get
        {
            var p = (PathBase ?? "").Trim().TrimEnd('/');
            return p.Length == 0 ? "" : p.StartsWith('/') ? p : "/" + p;
        }
    }
}

/// <summary>
/// Encabezados X-Forwarded-For / X-Forwarded-Proto / X-Forwarded-Host, para HTTPS terminado en un proxy
/// (nginx, IIS, Traefik, balanceador de nube).
/// </summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>Activa la lectura de los encabezados X-Forwarded-*.</summary>
    public bool Enabled { get; set; }

    /// <summary>IP de proxies de confianza. Si no se indica ninguno, solo se confía en localhost.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Redes de confianza en formato CIDR, p. ej. "10.0.0.0/8" (útil en Docker y Kubernetes).</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>Confía en cualquier proxy. Usar solo si la app no es accesible sin pasar por el proxy.</summary>
    public bool TrustAllProxies { get; set; }
}
