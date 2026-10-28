using Mantenedores.Core.Services;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Sección <c>Schema</c>.</summary>
public sealed class SchemaRefreshOptions
{
    public const string Section = "Schema";

    /// <summary>Cada cuántos minutos se vuelve a leer el catálogo de las conexiones abiertas (P-086). 0 = nunca.</summary>
    public int RefreshMinutes { get; set; } = 60;
}

/// <summary>
/// Detecta cambios de esquema sin que nadie pulse ↻ (P-086): relee el catálogo de las conexiones que están en memoria.
/// Si algo cambió, el aviso de cambios aparece solo; si no, no toca nada.
/// </summary>
public sealed class SchemaRefreshService(SchemaService schemas, SchemaRefreshOptions options, ILogger<SchemaRefreshService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RefreshMinutes <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.RefreshMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshAllAsync(CancellationToken ct)
    {
        foreach (var id in schemas.LoadedConnections)
        {
            try
            {
                var changes = await schemas.ReloadIfChangedAsync(id, ct);
                if (changes.Count > 0)
                    logger.LogInformation("El esquema de {Connection} cambió: {Count} cambios; se reemplazó el que estaba en memoria", id, changes.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Base caída o sin permiso: se mantiene lo que había y se reintenta en el próximo ciclo.
                logger.LogWarning(ex, "No se pudo volver a leer el esquema de {Connection}", id);
            }
        }
    }
}
