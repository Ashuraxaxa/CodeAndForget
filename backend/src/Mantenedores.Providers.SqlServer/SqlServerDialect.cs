using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.SqlServer;

public sealed class SqlServerDialect : ISqlDialect
{
    public string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>Filas del heap o del índice clúster: SQL Server las mantiene al día sin recorrer la tabla.</summary>
    public string? EstimatedRowCount(string schemaParam, string tableParam) =>
        "SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id " +
        $"JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = {schemaParam} AND t.name = {tableParam} AND p.index_id IN (0, 1)";

    public string CappedCount(string fromWhere, string limitParam) =>
        $"SELECT COUNT(*) FROM (SELECT TOP ({limitParam}) 1 AS x FROM {fromWhere}) c";

    public string Paging(string offsetParam, string limitParam) =>
        $"OFFSET {offsetParam} ROWS FETCH NEXT {limitParam} ROWS ONLY";

    public string Contains(string columnExpr, string param) =>
        $"CAST({columnExpr} AS nvarchar(max)) LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>decimal(29..38) puede no caber en decimal de .NET: se lee como texto (P-023).</summary>
    public string SelectExpression(ColumnInfo column, string columnExpr) =>
        column.IsLargeNumber ? $"CAST({columnExpr} AS varchar(50))"
        // Tipos CLR de SQL Server (P-018): geography/geometry como WKT y hierarchyid como ruta ("/1/2/").
        : column.Type == LogicalType.ReadOnlyText && column.NativeType.Equals("hierarchyid", StringComparison.OrdinalIgnoreCase)
            ? $"{columnExpr}.ToString()"
        : column.Type == LogicalType.ReadOnlyText ? $"{columnExpr}.STAsText()"
        : columnExpr;

    public string ByteLength(string columnExpr) => $"DATALENGTH({columnExpr})";

    /// <summary>Un nulo sin tipo llega como nvarchar, que SQL Server no convierte a varbinary: se fija el tipo (P-018).</summary>
    public string ParameterFor(ColumnInfo column, string param) =>
        column.Type == LogicalType.Binary ? $"CAST({param} AS varbinary(max))" : param;

    /// <summary>
    /// Usa OUTPUT ... INTO una variable de tabla: un OUTPUT directo falla si la tabla tiene triggers,
    /// algo muy común en bases SQL Server existentes.
    /// </summary>
    public string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns)
    {
        var declare = string.Join(", ", keyColumns.Select(k => $"{Quote(k.Name)} {DeclareType(k)}"));
        var output = string.Join(", ", keyColumns.Select(k => $"INSERTED.{Quote(k.Name)}"));
        var body = columns.Count == 0
            ? $"OUTPUT {output} INTO @__keys DEFAULT VALUES"
            : $"({string.Join(", ", columns)}) OUTPUT {output} INTO @__keys VALUES ({string.Join(", ", values)})";

        return $"DECLARE @__keys TABLE ({declare}); INSERT INTO {table} {body}; SELECT * FROM @__keys;";
    }

    /// <summary>Tipo SQL para declarar una columna clave en la variable de tabla.</summary>
    public static string DeclareType(ColumnInfo c)
    {
        var t = c.NativeType.ToLowerInvariant();
        return t switch
        {
            "varchar" or "char" or "nvarchar" or "nchar" or "varbinary" or "binary" =>
                $"{t}({(c.MaxLength is { } n ? n.ToString() : "max")})",
            "decimal" or "numeric" => $"{t}({c.Precision ?? 18},{c.Scale ?? 0})",
            "timestamp" or "rowversion" => "binary(8)",
            _ => t
        };
    }
}
