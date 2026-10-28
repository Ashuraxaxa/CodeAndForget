using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Providers;

/// <summary>
/// Diferencias de sintaxis entre motores (D-031). Todo nombre de tabla o columna pasa por <see cref="Quote"/>;
/// los valores siempre van como parámetros. En Core no hay ramas por motor: si un motor necesita algo distinto,
/// se agrega aquí como capacidad.
/// </summary>
public interface ISqlDialect
{
    /// <summary>Encierra un identificador entre los delimitadores del motor, escapándolo.</summary>
    string Quote(string identifier);

    string Table(string schema, string table) => $"{Quote(schema)}.{Quote(table)}";

    /// <summary>Prefijo de los parámetros en el texto SQL (@p0, :p0…).</summary>
    string ParameterPrefix => "@";

    /// <summary>Cláusula de paginación que va después del ORDER BY.</summary>
    string Paging(string offsetParam, string limitParam);

    /// <summary>
    /// Predicado "contiene" sin distinguir mayúsculas. El parámetro ya trae los % y usa
    /// <see cref="Query.QueryBuilder.LikeEscape"/> como carácter de escape.
    /// </summary>
    /// <param name="isText">La columna ya es texto: sin CAST, la base puede usar un índice (p. ej. trigramas, P-034).</param>
    string Contains(string columnExpr, string param, bool isText = false);

    /// <summary>Expresión para un parámetro de escritura o filtro (p. ej. CAST a jsonb o a un enum en Postgres).</summary>
    string ParameterFor(ColumnInfo column, string param) => param;

    /// <summary>
    /// Bloquea una fila hasta el fin de la transacción (P-096): entre comparar la versión y escribir, nadie más la cambia.
    /// Por defecto <c>SELECT … FOR UPDATE</c> (PostgreSQL, MySQL, MariaDB).
    /// </summary>
    string LockRow(string table, string alias, string where) => $"SELECT 1 FROM {table} {alias} WHERE {where} FOR UPDATE";

    /// <summary>Expresión para leer una columna (p. ej. CAST a texto de números que no caben en decimal).</summary>
    string SelectExpression(ColumnInfo column, string columnExpr) => columnExpr;

    /// <summary>
    /// INSERT que devuelve la PK como filas de resultado (RETURNING, OUTPUT…).
    /// Devuelve null si el motor no lo permite; Core usa entonces <see cref="LastInsertIdExpression"/>
    /// o los valores de la PK que vienen en el alta.
    /// </summary>
    /// <param name="table">Nombre de tabla ya delimitado.</param>
    /// <param name="columns">Columnas ya delimitadas.</param>
    /// <param name="values">Expresiones de valor (parámetros).</param>
    string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns);

    /// <summary>INSERT simple, sin devolver nada.</summary>
    string Insert(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values) =>
        columns.Count == 0
            ? $"INSERT INTO {table} DEFAULT VALUES"
            : $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

    /// <summary>Tamaño en bytes de un binario (P-018): para mostrar "12 KB" sin traer el archivo.</summary>
    string ByteLength(string columnExpr) => $"OCTET_LENGTH({columnExpr})";

    /// <summary>
    /// Consulta que devuelve las filas estimadas de una tabla según las estadísticas del motor (P-052, D-072), sin
    /// recorrerla. Esquema y tabla llegan como parámetros (son valores del catálogo, no identificadores).
    /// Null si el motor no tiene estimación: se usa el conteo exacto.
    /// </summary>
    string? EstimatedRowCount(string schemaParam, string tableParam) => null;

    /// <summary>
    /// Cuenta hasta <paramref name="limitParam"/> filas y para (D-072): en una tabla grande, con un término frecuente,
    /// no recorre toda la tabla. <paramref name="fromWhere"/> es "tabla alias WHERE …" ya armado.
    /// </summary>
    string CappedCount(string fromWhere, string limitParam) =>
        $"SELECT COUNT(*) FROM (SELECT 1 AS x FROM {fromWhere} LIMIT {limitParam}) c";

    /// <summary>Expresión que devuelve el último id autogenerado en la sesión (MySQL: LAST_INSERT_ID()). Null si no aplica.</summary>
    string? LastInsertIdExpression => null;
}
