using System.Data.Common;
using Mantenedores.Core.Providers;
using Npgsql;

namespace Mantenedores.Providers.Postgres;

public sealed class PostgresProvider : IDatabaseProvider
{
    public DatabaseEngine Engine => DatabaseEngine.Postgres;
    public ISqlDialect Dialect { get; } = new PostgresDialect();
    public ISchemaProvider Schema { get; } = new PostgresSchemaProvider();

    // Npgsql mantiene un pool por cadena de conexión.
    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);
}
