namespace Mantenedores.Core.Query;

public sealed record SortSpec(string Column, bool Descending = false)
{
    /// <summary>"-fecha,nombre" → fecha descendente, nombre ascendente.</summary>
    public static IReadOnlyList<SortSpec> ParseList(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.StartsWith('-') ? new SortSpec(s[1..].Trim(), true) : new SortSpec(s))
            .Where(s => s.Column.Length > 0)
            .ToList();
}

public sealed record ListRequest
{
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public IReadOnlyList<SortSpec> Sort { get; init; } = [];

    /// <summary>Filtro por columna: texto = contiene; otros tipos = igual.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Igualdad exacta por columna, también en textos (P-014): la usa el maestro-detalle para filtrar las hijas
    /// por su FK, incluso compuesta. Se combina con <see cref="Filters"/> y <see cref="Search"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Exact { get; init; } = new Dictionary<string, string>();

    /// <summary>Búsqueda general sobre todas las columnas de texto.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Paginación por clave (P-098): la PK de la última fila de la página anterior. Si el orden es el de la PK, la página
    /// se pide con <c>pk &gt; after</c> en vez de saltar filas con OFFSET, que en tablas enormes se vuelve lento.
    /// Si el orden es otro, se ignora y se usa <see cref="Page"/>.
    /// </summary>
    public string? After { get; init; }
}

/// <param name="TotalIsEstimate">El total viene de las estadísticas del motor, no de contar (tablas grandes, D-072).</param>
/// <param name="TotalIsCapped">Hay al menos <c>Total</c> filas; se dejó de contar ahí (tabla grande con filtros, D-072).</param>
public sealed record PagedResult(
    IReadOnlyList<IDictionary<string, object?>> Rows,
    long Total,
    int Page,
    int PageSize,
    bool TotalIsEstimate = false,
    bool TotalIsCapped = false);

public sealed record LookupItem(object? Value, string? Label);
