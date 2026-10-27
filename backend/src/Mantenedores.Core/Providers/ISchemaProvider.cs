using System.Data.Common;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Providers;

/// <summary>Lee el catálogo de un motor y devuelve el esquema normalizado.</summary>
public interface ISchemaProvider
{
    Task<DatabaseSchema> ReadAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct);
}
