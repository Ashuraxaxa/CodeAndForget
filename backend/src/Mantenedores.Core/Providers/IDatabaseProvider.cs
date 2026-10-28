using System.Data.Common;
using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Providers;

/// <summary>
/// Punto de extensión por motor. Agregar un motor = una implementación nueva, compilada con la app o cargada como
/// plugin desde una carpeta (P-064, D-092). Guía en <c>docs/motores.md</c>.
/// </summary>
public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }

    /// <summary>Nombre para la interfaz (p. ej. "MySQL / MariaDB").</summary>
    string DisplayName => Engine.Name;

    /// <summary>Cadena de ejemplo que la interfaz muestra al crear una conexión.</summary>
    string? ExampleConnectionString => null;
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

    /// <summary>
    /// Agrega a la cadena el tope del pool y el cierre de inactivas con los nombres de su driver (D-070),
    /// sin tocar lo que el usuario ya configuró.
    /// </summary>
    string ApplyPool(string connectionString, PoolOptions pool) => connectionString;

    /// <summary>Ajustes de sesión al abrir cada conexión (p. ej. zona horaria UTC en MySQL).</summary>
    Task InitializeSessionAsync(DbConnection connection, CancellationToken ct) => Task.CompletedTask;
}

public sealed class ProviderRegistry
{
    private readonly Dictionary<DatabaseEngine, IDatabaseProvider> _providers;

    public ProviderRegistry(IEnumerable<IDatabaseProvider> providers)
    {
        _providers = [];
        foreach (var p in providers)
            if (!_providers.TryAdd(p.Engine, p))
                throw new InvalidOperationException(
                    $"Dos proveedores dicen ser el motor '{p.Engine}': {_providers[p.Engine].GetType().FullName} y {p.GetType().FullName}.");
    }

    /// <summary>Los motores instalados, en el orden en que se registraron.</summary>
    public IReadOnlyList<IDatabaseProvider> All => _providers.Values.ToList();

    public bool Has(DatabaseEngine engine) => _providers.ContainsKey(engine);

    public IDatabaseProvider Get(DatabaseEngine engine) =>
        _providers.TryGetValue(engine, out var p)
            ? p
            : throw new NotSupportedException($"Motor no soportado: {engine}");
}
