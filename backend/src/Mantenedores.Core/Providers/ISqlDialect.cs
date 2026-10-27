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
    string Contains(string columnExpr, string param);

    /// <summary>Expresión para un parámetro de escritura o filtro (p. ej. CAST a jsonb o a un enum en Postgres).</summary>
    string ParameterFor(ColumnInfo column, string param) => param;

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

    /// <summary>Expresión que devuelve el último id autogenerado en la sesión (MySQL: LAST_INSERT_ID()). Null si no aplica.</summary>
    string? LastInsertIdExpression => null;
}
