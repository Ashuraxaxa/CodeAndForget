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

    public string ApplyPool(string connectionString, PoolOptions pool)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        PoolOptions.SetIfMissing(b, pool.MaxSize, "Maximum Pool Size", "Max Pool Size", "MaxPoolSize", "MaximumPoolSize");
        PoolOptions.SetIfMissing(b, pool.IdleSeconds, "Connection Idle Lifetime", "ConnectionIdleLifetime");
        return b.ConnectionString;
    }

    public static readonly Version MinimumVersion = new(12, 0);

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT current_setting('server_version_num')::int, current_setting('server_version'), " +
                          "current_setting('server_encoding'), current_setting('TimeZone')";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        var num = r.GetInt32(0); // p. ej. 120022 = 12.22, 160004 = 16.4
        return new ServerInfo("PostgreSQL", new Version(num / 10000, num % 10000), MinimumVersion,
            new Dictionary<string, string>
            {
                ["server_version"] = r.GetString(1),
                ["server_encoding"] = r.GetString(2),
                ["TimeZone"] = r.GetString(3)
            });
    }

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
