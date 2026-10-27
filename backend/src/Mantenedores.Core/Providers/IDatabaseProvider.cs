using System.Data.Common;

namespace Mantenedores.Core.Providers;

/// <summary>Punto de extensión por motor. Agregar MySQL u Oracle = una implementación nueva.</summary>
public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }
    ISqlDialect Dialect { get; }
    ISchemaProvider Schema { get; }
    DbConnection CreateConnection(string connectionString);
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
