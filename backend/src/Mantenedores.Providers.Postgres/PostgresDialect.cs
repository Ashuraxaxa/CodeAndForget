using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.Postgres;

public sealed class PostgresDialect : ISqlDialect
{
    public string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>reltuples: -1 si la tabla nunca se analizó (PostgreSQL 14+); entonces se cuenta exacto.</summary>
    public string? EstimatedRowCount(string schemaParam, string tableParam) =>
        "SELECT CAST(c.reltuples AS bigint) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
        $"WHERE n.nspname = {schemaParam} AND c.relname = {tableParam} AND c.relkind = 'r'";

    public string Paging(string offsetParam, string limitParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";

    public string Contains(string columnExpr, string param) =>
        $"CAST({columnExpr} AS text) ILIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    public string ParameterFor(ColumnInfo column, string param) => column.Type switch
    {
        // Números que no caben en decimal viajan como texto y se convierten aquí (P-023).
        LogicalType.Decimal when column.IsLargeNumber => $"CAST({param} AS numeric)",
        LogicalType.Json => $"CAST({param} AS {(column.NativeType == "json" ? "json" : "jsonb")})",
        LogicalType.Enum => $"CAST({param} AS {Quote(column.NativeTypeSchema ?? "public")}.{Quote(column.NativeType)})",
        // Arreglos (P-018): llegan como text[] y se convierten al arreglo de la columna (int4[], date[]…).
        LogicalType.Array => $"CAST({param} AS {Quote(column.ElementNativeTypeSchema ?? "pg_catalog")}.{Quote(column.ElementNativeType ?? "text")}[])",
        _ => param
    };

    /// <summary>
    /// Se leen como texto:
    /// - números que no caben en decimal de .NET (P-023);
    /// - enums: Npgsql guarda en caché los tipos al conectarse por primera vez, y un enum creado después
    ///   ("-.-" desconocido) no se podría leer. Como texto no depende de esa caché.
    /// </summary>
    public string SelectExpression(ColumnInfo column, string columnExpr) =>
        column.IsLargeNumber || column.Type == LogicalType.Enum ? $"CAST({columnExpr} AS text)"
        // PostGIS (P-018): geometry/geography como WKT, de solo lectura.
        : column.Type == LogicalType.ReadOnlyText ? $"ST_AsText({columnExpr})"
        // Arreglos (P-018) como text[]: Npgsql no lee un int4[] con nulos como int[], y la UI edita cada elemento como texto.
        : column.Type == LogicalType.Array ? $"CAST({columnExpr} AS text[])"
        : columnExpr;

    public string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns)
    {
        var returning = string.Join(", ", keyColumns.Select(k => Quote(k.Name)));
        return columns.Count == 0
            ? $"INSERT INTO {table} DEFAULT VALUES RETURNING {returning}"
            : $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}) RETURNING {returning}";
    }
}
