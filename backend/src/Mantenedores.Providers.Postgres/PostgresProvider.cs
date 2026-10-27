using System.Data.Common;
using Mantenedores.Core.Errors;
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

    /// <summary>SQLSTATE de la clase 23 (violación de integridad); Postgres informa el nombre de la restricción.</summary>
    public DbErrorInfo ClassifyError(DbException exception) => exception is not PostgresException pg
        ? DbErrorInfo.Unknown
        : pg.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new(DbErrorKind.Unique, pg.ConstraintName),
            PostgresErrorCodes.ForeignKeyViolation => new(DbErrorKind.ForeignKey, pg.ConstraintName),
            PostgresErrorCodes.CheckViolation => new(DbErrorKind.Check, pg.ConstraintName),
            PostgresErrorCodes.NotNullViolation => new(DbErrorKind.NotNull, Column: pg.ColumnName),
            _ => DbErrorInfo.Unknown
        };
}
