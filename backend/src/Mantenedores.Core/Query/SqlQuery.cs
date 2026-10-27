namespace Mantenedores.Core.Query;

/// <summary>SQL generado más sus parámetros. Los valores nunca se concatenan en el texto.</summary>
/// <param name="Renames">Alias cortos del SELECT y el nombre con que se devuelven (p. ej. "__lbl0" → "categoria_id__label").</param>
public sealed record SqlQuery(
    string Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyDictionary<string, string>? Renames = null);

/// <summary>INSERT armado. Si <paramref name="ProvidedKeys"/> no es null, la clave no la devuelve la BD: es la que vino en el alta.</summary>
public sealed record InsertQuery(SqlQuery Query, IReadOnlyDictionary<string, object?>? ProvidedKeys);
