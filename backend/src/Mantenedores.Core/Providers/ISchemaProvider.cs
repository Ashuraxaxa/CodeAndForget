using System.Data.Common;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Providers;

/// <summary>Lee el catálogo de un motor y devuelve el esquema normalizado.</summary>
public interface ISchemaProvider
{
    Task<DatabaseSchema> ReadAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct);

    /// <summary>
    /// Procedimientos y funciones con sus parámetros de entrada (P-061). Sin los del sistema ni los de extensiones.
    /// Una rutina sobrecargada (mismo nombre, distintos parámetros) no se incluye: no se sabría a cuál llamar.
    /// </summary>
    Task<IReadOnlyList<RoutineInfo>> ReadRoutinesAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RoutineInfo>>([]);
}
