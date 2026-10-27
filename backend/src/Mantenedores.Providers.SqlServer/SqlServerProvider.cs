using System.Data.Common;
using Mantenedores.Core.Providers;
using Microsoft.Data.SqlClient;

namespace Mantenedores.Providers.SqlServer;

public sealed class SqlServerProvider : IDatabaseProvider
{
    public DatabaseEngine Engine => DatabaseEngine.SqlServer;
    public ISqlDialect Dialect { get; } = new SqlServerDialect();
    public ISchemaProvider Schema { get; } = new SqlServerSchemaProvider();

    // SqlClient mantiene un pool por cadena de conexión.
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);
}
