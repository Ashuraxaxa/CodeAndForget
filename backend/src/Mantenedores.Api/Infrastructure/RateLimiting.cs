using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Límites de uso (P-051, P-091, D-070), sección <c>RateLimit</c>. En memoria de cada instancia.</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    public bool Enabled { get; set; } = true;

    /// <summary>Solicitudes a la API por minuto y por usuario (o por IP sin sesión).</summary>
    public int PermitsPerMinute { get; set; } = 600;

    /// <summary>Exportaciones e importaciones por minuto y por usuario: son las operaciones más pesadas.</summary>
    public int HeavyPerMinute { get; set; } = 10;

    /// <summary>Intentos de inicio de sesión por minuto y por IP.</summary>
    public int LoginPerMinute { get; set; } = 10;
}

public static class RateLimiting
{
    public const string Login = "login";
    public const string Heavy = "heavy";

    public static void AddMantenedoresRateLimiting(this IServiceCollection services, RateLimitOptions o)
    {
        services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            r.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString();
                await Results.Problem("Demasiadas solicitudes seguidas. Espere un momento y vuelva a intentar.",
                    statusCode: 429, title: "Límite de uso").ExecuteAsync(ctx.HttpContext);
            };

            if (!o.Enabled)
            {
                // Las políticas tienen que existir aunque no limiten: los endpoints las nombran.
                r.AddPolicy(Login, _ => RateLimitPartition.GetNoLimiter("off"));
                r.AddPolicy(Heavy, _ => RateLimitPartition.GetNoLimiter("off"));
                return;
            }

            // Solo la API: los archivos del frontend y los health checks (que el orquestador consulta seguido) no cuentan.
            r.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                !http.Request.Path.StartsWithSegments("/api") || http.Request.Path.StartsWithSegments("/api/health")
                    ? RateLimitPartition.GetNoLimiter("")
                    : Window(Who(http), o.PermitsPerMinute));
            r.AddPolicy(Heavy, http => Window(Who(http), o.HeavyPerMinute));
            r.AddPolicy(Login, http => Window("ip:" + http.Connection.RemoteIpAddress, o.LoginPerMinute));
        });
    }

    /// <summary>El usuario de la sesión; sin sesión, la IP (ya corregida por el proxy, P-007).</summary>
    private static string Who(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.Name) is { Length: > 0 } name ? "user:" + name.ToLowerInvariant()
            : "ip:" + http.Connection.RemoteIpAddress;

    private static RateLimitPartition<string> Window(string key, int permits) =>
        permits <= 0
            ? RateLimitPartition.GetNoLimiter(key)
            : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
            });
}
