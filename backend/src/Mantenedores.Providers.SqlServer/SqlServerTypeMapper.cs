using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.SqlServer;

public static class SqlServerTypeMapper
{
    /// <param name="typeName">Tipo de sistema (los tipos alias ya vienen resueltos a su base).</param>
    /// <param name="maxLength">Largo en caracteres; null = (max).</param>
    public static (LogicalType Type, bool HasTimeZone) Map(string typeName, int? maxLength) =>
        typeName.ToLowerInvariant() switch
        {
            "tinyint" or "smallint" or "int" or "bigint" => (LogicalType.Integer, false),
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => (LogicalType.Decimal, false),
            "bit" => (LogicalType.Boolean, false),
            "char" or "varchar" or "nchar" or "nvarchar" =>
                (maxLength is null ? LogicalType.Text : LogicalType.String, false),
            "text" or "ntext" or "xml" => (LogicalType.Text, false),
            "date" => (LogicalType.Date, false),
            "datetime" or "datetime2" or "smalldatetime" => (LogicalType.DateTime, false),
            "datetimeoffset" => (LogicalType.DateTime, true),
            "time" => (LogicalType.Time, false),
            "uniqueidentifier" => (LogicalType.Uuid, false),
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => (LogicalType.Binary, false),
            // geography, geometry, hierarchyid, sql_variant: requieren tratamiento especial (Fase 2).
            _ => (LogicalType.Unsupported, false)
        };

    /// <summary>sys.columns.max_length está en bytes y -1 significa (max).</summary>
    public static int? CharLength(string typeName, int maxLengthBytes) =>
        maxLengthBytes == -1 ? null
        : typeName.ToLowerInvariant() is "nchar" or "nvarchar" ? maxLengthBytes / 2
        : maxLengthBytes;
}
