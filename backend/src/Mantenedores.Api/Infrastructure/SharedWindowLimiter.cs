using System.Threading.RateLimiting;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Ventana fija con el contador en la caché compartida (P-050): todas las instancias suman al mismo número.
/// Si la caché no responde, cuenta en memoria con una ventana igual, como sin Redis.
/// </summary>
/// <remarks>
/// <see cref="AttemptAcquireCore"/> no puede esperar a Redis, así que siempre responde "no" sin contar; el middleware
/// de ASP.NET entonces llama a <see cref="AcquireAsyncCore"/>, que es donde se cuenta. Así cada solicitud suma una vez.
/// </remarks>
public sealed class SharedWindowLimiter(IRateCounter counter, string key, FixedWindowRateLimiterOptions options) : RateLimiter
{
    private readonly FixedWindowRateLimiter _local = new(options);
    private long _lastUse = Environment.TickCount64;

    public override TimeSpan? IdleDuration => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastUse));

    public override RateLimiterStatistics? GetStatistics() => _local.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Lease.NotYet;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken ct)
    {
        Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
        var window = options.Window;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var slot = now / (long)window.TotalMilliseconds;
        var count = await counter.IncrementAsync($"{key}:{slot}", permitCount, window, ct);
        if (count is null) return await _local.AcquireAsync(permitCount, ct);
        if (count <= options.PermitLimit) return Lease.Granted;
        var retry = TimeSpan.FromMilliseconds((slot + 1) * (long)window.TotalMilliseconds - now);
        return new Lease(false, retry);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _local.Dispose();
        base.Dispose(disposing);
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter = null) : RateLimitLease
    {
        public static readonly Lease Granted = new(true);
        public static readonly Lease NotYet = new(false);

        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = retryAfter;
            return retryAfter is not null && metadataName == MetadataName.RetryAfter.Name;
        }
    }
}
