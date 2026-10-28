namespace Mantenedores.Core.Schema;

public sealed record ForeignKeyInfo
{
    public required string Name { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required string RefSchema { get; init; }
    public required string RefTable { get; init; }
    public required IReadOnlyList<string> RefColumns { get; init; }

    public bool IsSingleColumn => Columns.Count == 1;

    /// <summary>
    /// Columna local donde va la etiqueta (<c>&lt;columna&gt;__label</c>, P-030): la única, o la última de una FK compuesta
    /// (en (pais, sucursal_codigo), la que identifica a la sucursal dentro del país).
    /// </summary>
    public string LabelColumn => Columns[^1];
}
