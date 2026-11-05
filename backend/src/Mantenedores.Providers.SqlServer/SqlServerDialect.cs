using System.Text.RegularExpressions;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.SqlServer;

public sealed partial class SqlServerDialect : ISqlDialect
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

    public string Contains(string columnExpr, string param, bool isText = false) =>
        $"{(isText ? columnExpr : $"CAST({columnExpr} AS nvarchar(max))")} LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>Sin tildes ni mayúsculas (P-109): una intercalación _CI_AI, que existe desde siempre.</summary>
    public string ContainsIgnoringAccents(string columnExpr, string param, bool isText = false) =>
        $"{(isText ? columnExpr : $"CAST({columnExpr} AS nvarchar(max))")} COLLATE Latin1_General_CI_AI LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>decimal(29..38) puede no caber en decimal de .NET: se lee como texto (P-023).</summary>
    public string SelectExpression(ColumnInfo column, string columnExpr) =>
        column.IsLargeNumber ? $"CAST({columnExpr} AS varchar(50))"
        // Tipos CLR de SQL Server (P-018): geography/geometry como WKT y hierarchyid como ruta ("/1/2/").
        : column.Type == LogicalType.ReadOnlyText && column.NativeType.Equals("hierarchyid", StringComparison.OrdinalIgnoreCase)
            ? $"{columnExpr}.ToString()"
        : column.Type is LogicalType.ReadOnlyText or LogicalType.Spatial ? $"{columnExpr}.STAsText()"
        : columnExpr;

    public string ByteLength(string columnExpr) => $"DATALENGTH({columnExpr})";

    /// <summary>Procedimiento con sus parámetros por nombre (P-061): <c>EXEC esquema.proc @id = @a0, …</c>.</summary>
    /// Los parámetros no se pueden delimitar con corchetes: el nombre viene del catálogo y igual se valida.
    public string CallRoutine(string routine, RoutineKind kind, IReadOnlyList<(RoutineParameter Parameter, string Placeholder)> args) =>
        $"EXEC {routine} {string.Join(", ", args.Select(a => $"@{ParameterName(a.Parameter.Name)} = {a.Placeholder}"))}".TrimEnd();

    private static string ParameterName(string name) => ParameterNamePattern().IsMatch(name)
        ? name : throw new InvalidOperationException($"Nombre de parámetro inesperado: {name}");

    [GeneratedRegex(@"^[\p{L}_#$][\p{L}\p{N}_#$@]*$")]
    private static partial Regex ParameterNamePattern();

    /// <summary>SQL Server no tiene FOR UPDATE: el bloqueo de actualización se pide con una sugerencia de tabla (P-096).</summary>
    public string LockRow(string table, string alias, string where) =>
        $"SELECT 1 FROM {table} {alias} WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE {where}";

    /// <summary>Un nulo sin tipo llega como nvarchar, que SQL Server no convierte a varbinary: se fija el tipo (P-018).</summary>
    /// <summary>
    /// Geography/geometry desde WKT (P-090): geography en WGS 84 (SRID 4326, el de los GPS), geometry sin sistema (0).
    /// </summary>
    public string ParameterFor(ColumnInfo column, string param) =>
        column.Type == LogicalType.Binary ? $"CAST({param} AS varbinary(max))"
        : column.Type == LogicalType.Spatial
            ? column.NativeType.Equals("geography", StringComparison.OrdinalIgnoreCase)
                ? $"geography::STGeomFromText(CAST({param} AS nvarchar(max)), 4326)"
                : $"geometry::STGeomFromText(CAST({param} AS nvarchar(max)), 0)"
        : param;

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

    /// <summary>
    /// Sin DATETRUNC (es de 2022, D-035): DATEFROMPARTS para mes, trimestre y año; la semana cuenta días desde el lunes
    /// 1900-01-01, sin depender de SET DATEFIRST. Con zona horaria, en UTC (P-127).
    /// </summary>
    public string DateTrunc(ColumnInfo column, string columnExpr, DateBucket bucket)
    {
        var e = column.HasTimeZone ? $"SWITCHOFFSET({columnExpr}, '+00:00')" : columnExpr;
        return bucket switch
        {
            DateBucket.Day => $"CAST({e} AS date)",
            DateBucket.Week => $"DATEADD(day, DATEDIFF(day, CAST('19000101' AS date), CAST({e} AS date)) / 7 * 7, CAST('19000101' AS date))",
            DateBucket.Month => $"DATEFROMPARTS(YEAR({e}), MONTH({e}), 1)",
            DateBucket.Quarter => $"DATEFROMPARTS(YEAR({e}), (DATEPART(quarter, {e}) - 1) * 3 + 1, 1)",
            _ => $"DATEFROMPARTS(YEAR({e}), 1, 1)"
        };
    }
}
