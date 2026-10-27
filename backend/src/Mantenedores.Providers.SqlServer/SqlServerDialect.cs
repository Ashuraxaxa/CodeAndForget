using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.SqlServer;

public sealed class SqlServerDialect : ISqlDialect
{
    public string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    public string Paging(string offsetParam, string limitParam) =>
        $"OFFSET {offsetParam} ROWS FETCH NEXT {limitParam} ROWS ONLY";

    public string Contains(string columnExpr, string param) =>
        $"CAST({columnExpr} AS nvarchar(max)) LIKE {param} ESCAPE '\\'";

    /// <summary>
    /// Usa OUTPUT ... INTO una variable de tabla: un OUTPUT directo falla si la tabla tiene triggers,
    /// algo muy común en bases SQL Server existentes.
    /// </summary>
    public string InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns)
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
