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
