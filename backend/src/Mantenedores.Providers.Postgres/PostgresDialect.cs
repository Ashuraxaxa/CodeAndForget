using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.Postgres;

public sealed class PostgresDialect : ISqlDialect
{
    public string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public string Paging(string offsetParam, string limitParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";

    public string Contains(string columnExpr, string param) => $"CAST({columnExpr} AS text) ILIKE {param} ESCAPE '\\'";

    public string ParameterFor(ColumnInfo column, string param) => column.Type switch
    {
        LogicalType.Json => $"CAST({param} AS {(column.NativeType == "json" ? "json" : "jsonb")})",
        LogicalType.Enum => $"CAST({param} AS {Quote(column.NativeTypeSchema ?? "public")}.{Quote(column.NativeType)})",
        _ => param
    };

    public string InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns)
    {
        var returning = string.Join(", ", keyColumns.Select(k => Quote(k.Name)));
        return columns.Count == 0
            ? $"INSERT INTO {table} DEFAULT VALUES RETURNING {returning}"
            : $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}) RETURNING {returning}";
    }
}
