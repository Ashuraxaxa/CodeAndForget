using Mantenedores.Api.Auth;
using Mantenedores.Core.Security;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Cada cuánto revisa una instancia los cambios de las demás (D-077).</summary>
public sealed class ChangeSyncOptions
{
    /// <summary><c>Metadata:SyncSeconds</c>. 0 = no revisar (una sola instancia).</summary>
    public int SyncSeconds { get; set; } = 5;
}

/// <summary>
/// Con metadata compartida (Sqlite, Postgres o SQL Server), revisa cada pocos segundos si otra instancia cambió
/// ajustes, el esquema de una conexión o la seguridad, y descarta lo que esta tiene en memoria (P-082).
/// </summary>
public sealed class ChangeSyncService(
    ChangeStamps stamps, SchemaService schemas, SecurityService security, TokenService tokens,
    ChangeSyncOptions options, ILogger<ChangeSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!stamps.Enabled || options.SyncSeconds <= 0) return;
        var every = TimeSpan.FromSeconds(options.SyncSeconds);
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var scope in await stamps.PollAsync(stoppingToken))
                {
                    if (scope == ChangeStamps.Security) { security.Invalidate(); tokens.InvalidateUsers(); }
                    else schemas.ApplyRemoteChange(scope);
                    logger.LogDebug("Cambio de otra instancia: {Scope}", scope);
                }
                if (failing) logger.LogInformation("Se volvió a leer los avisos de cambios de otras instancias");
                failing = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un solo aviso por racha de errores, para no llenar el log si la metadata no responde.
                if (!failing) logger.LogWarning(ex, "No se pudieron leer los avisos de cambios de otras instancias; se reintenta");
                failing = true;
            }
            try { await Task.Delay(every, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
