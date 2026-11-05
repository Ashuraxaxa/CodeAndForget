using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.MySql;

/// <summary>
/// Dialecto de MySQL 8.0+ y MariaDB 10.6+. Diferencias con los otros motores (ver docs/motores.md):
/// identificadores con backticks, sin RETURNING (la clave sale de LAST_INSERT_ID()) y sin DEFAULT VALUES.
/// </summary>
public sealed class MySqlDialect : ISqlDialect
{
    public string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";

    /// <summary>TABLE_ROWS de InnoDB es una estimación (puede desviarse bastante): por eso la UI la muestra con "≈".</summary>
    public string? EstimatedRowCount(string schemaParam, string tableParam) =>
        $"SELECT TABLE_ROWS FROM information_schema.TABLES WHERE TABLE_SCHEMA = {schemaParam} AND TABLE_NAME = {tableParam} AND TABLE_TYPE = 'BASE TABLE'";

    public string Paging(string offsetParam, string limitParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";

    /// <summary>
    /// CAST a CHAR usa la intercalación de la conexión (por defecto *_ci: sin distinguir mayúsculas).
    /// El escape '!' no depende de NO_BACKSLASH_ESCAPES.
    /// </summary>
    public string Contains(string columnExpr, string param, bool isText = false) =>
        $"{(isText ? columnExpr : $"CAST({columnExpr} AS CHAR)")} LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>
    /// Sin tildes (P-109): utf8mb4_unicode_ci existe en MySQL 8 y en MariaDB 10.6 (utf8mb4_0900_ai_ci, solo en MySQL).
    /// CONVERT primero, por si la columna está en otro juego de caracteres (latin1…).
    /// </summary>
    public string ContainsIgnoringAccents(string columnExpr, string param, bool isText = false) =>
        $"CONVERT({columnExpr} USING utf8mb4) COLLATE utf8mb4_unicode_ci LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>
    /// Números grandes: sin CAST, MySQL compararía texto contra DECIMAL como DOUBLE y perdería precisión.
    /// </summary>
    /// <summary>Espaciales desde WKT (P-090), sin sistema de referencia (SRID 0, el de una columna sin SRID).</summary>
    public string ParameterFor(ColumnInfo column, string param) =>
        column is { Type: LogicalType.Decimal, IsLargeNumber: true } ? $"CAST({param} AS DECIMAL(65,30))"
        // Con SRID de columna (MySQL 8, P-104): el WKT va en longitud latitud, como en los demás motores.
        : column.Type == LogicalType.Spatial
            ? column.Srid is > 0 ? $"ST_GeomFromText({param}, {column.Srid}, 'axis-order=long-lat')" : $"ST_GeomFromText({param})"
        : param;

    public string SelectExpression(ColumnInfo column, string columnExpr) =>
        column.IsLargeNumber ? $"CAST({columnExpr} AS CHAR)"
        // Tipos espaciales (P-018, P-090): como WKT.
        : column.Type == LogicalType.Spatial && column.Srid is > 0 ? $"ST_AsText({columnExpr}, 'axis-order=long-lat')"
        : column.Type is LogicalType.ReadOnlyText or LogicalType.Spatial ? $"ST_AsText({columnExpr})"
        : columnExpr;

    /// <summary>MySQL no admite argumentos por nombre (P-061): van por posición. Una función se llama con SELECT.</summary>
    public string CallRoutine(string routine, RoutineKind kind, IReadOnlyList<(RoutineParameter Parameter, string Placeholder)> args)
    {
        var list = string.Join(", ", args.Select(a => a.Placeholder));
        return kind == RoutineKind.Procedure ? $"CALL {routine}({list})" : $"SELECT {routine}({list}) AS {Quote("resultado")}";
    }

    /// <summary>MySQL no tiene RETURNING (MariaDB sí, pero se usa LAST_INSERT_ID() en ambos, D-040).</summary>
    public string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values,
        IReadOnlyList<ColumnInfo> keyColumns) => null;

    /// <summary>MySQL no acepta DEFAULT VALUES: se usa "() VALUES ()".</summary>
    public string Insert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values) =>
        $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

    public string? LastInsertIdExpression => "LAST_INSERT_ID()";

    /// <summary>Con DATE, WEEKDAY (lunes = 0) y MAKEDATE; sin DATE_FORMAT, para no depender de cadenas (P-127).</summary>
    public string DateTrunc(ColumnInfo column, string columnExpr, DateBucket bucket) => bucket switch
    {
        DateBucket.Day => $"DATE({columnExpr})",
        DateBucket.Week => $"DATE_SUB(DATE({columnExpr}), INTERVAL WEEKDAY({columnExpr}) DAY)",
        DateBucket.Month => $"DATE_SUB(DATE({columnExpr}), INTERVAL DAYOFMONTH({columnExpr}) - 1 DAY)",
        DateBucket.Quarter => $"DATE_ADD(MAKEDATE(YEAR({columnExpr}), 1), INTERVAL QUARTER({columnExpr}) - 1 QUARTER)",
        _ => $"MAKEDATE(YEAR({columnExpr}), 1)"
    };
}
