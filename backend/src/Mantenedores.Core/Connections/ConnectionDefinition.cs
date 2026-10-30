using Mantenedores.Core.Providers;

namespace Mantenedores.Core.Connections;

public sealed record ConnectionDefinition
{
    /// <summary>Identificador corto usado en las rutas de la API (p. ej. "ventas").</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DatabaseEngine Engine { get; init; }
    public required string ConnectionString { get; init; }

    /// <summary>Esquemas a exponer. Vacío = todos.</summary>
    public IReadOnlyList<string> Schemas { get; init; } = [];

    /// <summary>
    /// ¿Una IA conectada por MCP puede ver una muestra de datos (las 1.000 filas más recientes de cada tabla, P-113)?
    /// Apagado por defecto: sin esto solo ve la estructura.
    /// </summary>
    public bool AllowSamples { get; init; }
}

public interface IConnectionStore
{
    Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct);
    Task<ConnectionDefinition?> GetAsync(string id, CancellationToken ct);
    Task SaveAsync(ConnectionDefinition connection, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}
