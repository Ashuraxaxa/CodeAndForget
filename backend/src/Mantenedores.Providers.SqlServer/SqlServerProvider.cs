using System.Data.Common;
using System.Text.RegularExpressions;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Microsoft.Data.SqlClient;

namespace Mantenedores.Providers.SqlServer;

public sealed partial class SqlServerProvider : IDatabaseProvider
{
    public DatabaseEngine Engine => DatabaseEngine.SqlServer;
    public ISqlDialect Dialect { get; } = new SqlServerDialect();
    public ISchemaProvider Schema { get; } = new SqlServerSchemaProvider();

    // SqlClient mantiene un pool por cadena de conexión.
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    /// <summary>SqlClient cierra solo las conexiones inactivas (a los 4 a 8 minutos): aquí se fija solo el tope.</summary>
    public string ApplyPool(string connectionString, PoolOptions pool)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        PoolOptions.SetIfMissing(b, pool.MaxSize, "Max Pool Size", "MaxPoolSize");
        return b.ConnectionString;
    }

    /// <summary>13 = SQL Server 2016.</summary>
    public static readonly Version MinimumVersion = new(13, 0);

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), " +
                          "CAST(SERVERPROPERTY('Edition') AS nvarchar(128)), " +
                          "CAST(SERVERPROPERTY('Collation') AS nvarchar(128)), " +
                          "CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        var version = Version.Parse(r.GetString(0)); // p. ej. 14.0.3456.2 = SQL Server 2017
        return new ServerInfo("SQL Server", new Version(version.Major, version.Minor), MinimumVersion,
            new Dictionary<string, string>
            {
                ["ProductVersion"] = r.GetString(0),
                ["Edition"] = r.GetString(1),
                ["ServerCollation"] = r.GetString(2),
                ["DatabaseCollation"] = r.IsDBNull(3) ? "" : r.GetString(3)
            });
    }

    /// <summary>
    /// SQL Server no expone el nombre de la restricción como dato: va entre comillas dentro del mensaje.
    /// Se clasifica por número de error y se extrae lo que está entre comillas, que no depende del idioma.
    /// 547 cubre FK y CHECK: el traductor las distingue buscando el nombre entre las FK de la tabla.
    /// </summary>
    public DbErrorInfo ClassifyError(DbException exception) => exception is not SqlException sql
        ? DbErrorInfo.Unknown
        : sql.Number switch
        {
            2627 => new(DbErrorKind.Unique, Quoted(sql.Message, 0)),           // PK o UNIQUE constraint
            2601 => new(DbErrorKind.Unique, Quoted(sql.Message, 1)),           // índice único: 'tabla', 'índice'
            547 => new(DbErrorKind.ForeignKeyOrCheck, DoubleQuoted(sql.Message, 0)), // FK o CHECK: lo resuelve la metadata
            515 => new(DbErrorKind.NotNull, Column: Quoted(sql.Message, 0)),
            229 or 230 => new(DbErrorKind.PermissionDenied),                  // permiso denegado en el objeto o la columna
            207 or 208 => new(DbErrorKind.SchemaMismatch),                    // columna u objeto inexistente
            _ => DbErrorInfo.Unknown
        };

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex SingleQuotes();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex DoubleQuotes();

    private static string? Quoted(string message, int index) => Nth(SingleQuotes().Matches(message), index);
    private static string? DoubleQuoted(string message, int index) => Nth(DoubleQuotes().Matches(message), index);
    private static string? Nth(MatchCollection m, int i) => m.Count > i ? m[i].Groups[1].Value : null;
}
