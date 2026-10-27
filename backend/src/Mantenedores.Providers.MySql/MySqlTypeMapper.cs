using System.Text.RegularExpressions;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.MySql;

public static partial class MySqlTypeMapper
{
    /// <param name="dataType">information_schema.COLUMNS.DATA_TYPE (p. ej. "int", "varchar").</param>
    /// <param name="columnType">COLUMNS.COLUMN_TYPE completo (p. ej. "tinyint(1)", "int unsigned", "enum('a','b')").</param>
    public static LogicalType Map(string dataType, string columnType, int? charLength)
    {
        var t = dataType.ToLowerInvariant();
        var full = columnType.ToLowerInvariant();
        var unsigned = full.Contains("unsigned");

        return t switch
        {
            // Convenciones de MySQL: TINYINT(1)/BOOLEAN y BIT(1) son booleanos.
            "tinyint" when full.StartsWith("tinyint(1)") => LogicalType.Boolean,
            "bit" when full == "bit(1)" => LogicalType.Boolean,
            // BIGINT UNSIGNED no cabe en long: se trata como decimal.
            "bigint" when unsigned => LogicalType.Decimal,
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint" or "year" => LogicalType.Integer,
            "decimal" or "numeric" or "float" or "double" or "real" => LogicalType.Decimal,
            // CHAR(36) es la forma usual de guardar UUID en MySQL; MySqlConnector lo lee como Guid.
            "char" when charLength == 36 => LogicalType.Uuid,
            "char" or "varchar" or "set" => LogicalType.String,
            "tinytext" or "text" or "mediumtext" or "longtext" => LogicalType.Text,
            "json" => LogicalType.Json,
            "enum" => LogicalType.Enum,
            "date" => LogicalType.Date,
            "datetime" or "timestamp" => LogicalType.DateTime,
            "time" => LogicalType.Time,
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => LogicalType.Binary,
            // Tipos espaciales (P-018): se leen como WKT y no se editan.
            "geometry" or "point" or "linestring" or "polygon" or "multipoint" or "multilinestring" or "multipolygon"
                or "geometrycollection" or "geomcollection" => LogicalType.ReadOnlyText,
            _ => LogicalType.Unsupported
        };
    }

    /// <summary>
    /// Nombre nativo que usa Core para elegir el tamaño del entero. Los TINYINT con signo y los UNSIGNED
    /// no coinciden con los rangos de SQL Server: se informan con su modificador para que se envíen como long.
    /// </summary>
    public static string NativeType(string dataType, string columnType)
    {
        var t = dataType.ToLowerInvariant();
        var full = columnType.ToLowerInvariant();
        if (full.Contains("unsigned")) return t + " unsigned";
        return t == "tinyint" ? "tinyint signed" : t;
    }

    [GeneratedRegex(@"'((?:[^']|'')*)'")]
    private static partial Regex EnumValue();

    /// <summary>Valores de enum('a','b''c') leídos de COLUMN_TYPE.</summary>
    public static IReadOnlyList<string> EnumValues(string columnType) =>
        EnumValue().Matches(columnType).Select(m => m.Groups[1].Value.Replace("''", "'")).ToList();
}
