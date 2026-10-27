namespace Mantenedores.Core.Query;

/// <summary>SQL generado más sus parámetros. Los valores nunca se concatenan en el texto.</summary>
public sealed record SqlQuery(string Sql, IReadOnlyDictionary<string, object?> Parameters);
