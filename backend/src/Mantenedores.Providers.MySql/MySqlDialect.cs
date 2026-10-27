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

    public string Paging(string offsetParam, string limitParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";

    /// <summary>
    /// CAST a CHAR usa la intercalación de la conexión (por defecto *_ci: sin distinguir mayúsculas).
    /// El escape '!' no depende de NO_BACKSLASH_ESCAPES.
    /// </summary>
    public string Contains(string columnExpr, string param) =>
        $"CAST({columnExpr} AS CHAR) LIKE {param} ESCAPE '{QueryBuilder.LikeEscape}'";

    /// <summary>
    /// Números grandes: sin CAST, MySQL compararía texto contra DECIMAL como DOUBLE y perdería precisión.
    /// </summary>
    public string ParameterFor(ColumnInfo column, string param) =>
        column is { Type: LogicalType.Decimal, IsLargeNumber: true } ? $"CAST({param} AS DECIMAL(65,30))" : param;

    public string SelectExpression(ColumnInfo column, string columnExpr) =>
        column.IsLargeNumber ? $"CAST({columnExpr} AS CHAR)" : columnExpr;

    /// <summary>MySQL no tiene RETURNING (MariaDB sí, pero se usa LAST_INSERT_ID() en ambos, D-040).</summary>
    public string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values,
        IReadOnlyList<ColumnInfo> keyColumns) => null;

    /// <summary>MySQL no acepta DEFAULT VALUES: se usa "() VALUES ()".</summary>
    public string Insert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values) =>
        $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

    public string? LastInsertIdExpression => "LAST_INSERT_ID()";
}
