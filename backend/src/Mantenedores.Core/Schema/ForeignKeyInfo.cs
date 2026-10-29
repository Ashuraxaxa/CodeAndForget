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

    /// <summary>
    /// Filtros del combo (P-110, D-097): la tabla referenciada se acota por columnas de este registro. En
    /// CentroCosto.AreaID → Area, el filtro (Area.EmpresaID = EmpresaID) muestra solo las áreas de la empresa elegida.
    /// Vienen del ajuste <c>lookupFilters</c>; la base no los declara.
    /// </summary>
    public IReadOnlyList<LookupFilter> LookupFilters { get; init; } = [];
}

/// <param name="RefColumn">Columna de la tabla referenciada (Area.EmpresaID).</param>
/// <param name="LocalColumn">Columna de este registro que da el valor (CentroCosto.EmpresaID).</param>
public sealed record LookupFilter(string RefColumn, string LocalColumn);
