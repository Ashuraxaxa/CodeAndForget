using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Providers;

/// <summary>
/// Diferencias de sintaxis entre motores. Todo nombre de tabla o columna pasa por <see cref="Quote"/>;
/// los valores siempre van como parámetros.
/// </summary>
public interface ISqlDialect
{
    /// <summary>Encierra un identificador entre los delimitadores del motor, escapándolo.</summary>
    string Quote(string identifier);

    string Table(string schema, string table) => $"{Quote(schema)}.{Quote(table)}";

    /// <summary>Cláusula de paginación que va después del ORDER BY.</summary>
    string Paging(string offsetParam, string limitParam);

    /// <summary>Predicado "contiene" sin distinguir mayúsculas. El parámetro ya trae los % y el escape.</summary>
    string Contains(string columnExpr, string param);

    /// <summary>Expresión para un parámetro de escritura (p. ej. CAST a jsonb o a un enum en Postgres).</summary>
    string ParameterFor(ColumnInfo column, string param) => param;

    /// <summary>INSERT que devuelve los valores de la PK de la fila creada.</summary>
    /// <param name="table">Nombre de tabla ya delimitado.</param>
    /// <param name="columns">Columnas ya delimitadas.</param>
    /// <param name="values">Expresiones de valor (parámetros).</param>
    string InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values, IReadOnlyList<ColumnInfo> keyColumns);
}
