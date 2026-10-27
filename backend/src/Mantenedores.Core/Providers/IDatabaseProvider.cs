using System.Data.Common;
using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Providers;

/// <summary>Punto de extensión por motor. Agregar MySQL u Oracle = una implementación nueva.</summary>
public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }
    ISqlDialect Dialect { get; }
    ISchemaProvider Schema { get; }
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Clasifica un error del motor por su código (no por el texto, que depende del idioma del servidor).
    /// Devuelve <see cref="DbErrorInfo.Unknown"/> si no es una violación de restricción conocida.
    /// </summary>
    DbErrorInfo ClassifyError(DbException exception);

    /// <summary>Producto, versión y ajustes relevantes del servidor.</summary>
    Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct);

    /// <summary>Ajustes de sesión al abrir cada conexión (p. ej. zona horaria UTC en MySQL).</summary>
    Task InitializeSessionAsync(DbConnection connection, CancellationToken ct) => Task.CompletedTask;
}

public sealed class ProviderRegistry
{
    private readonly Dictionary<DatabaseEngine, IDatabaseProvider> _providers;

    public ProviderRegistry(IEnumerable<IDatabaseProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Engine);

    public IDatabaseProvider Get(DatabaseEngine engine) =>
        _providers.TryGetValue(engine, out var p)
            ? p
            : throw new NotSupportedException($"Motor no soportado: {engine}");
}
