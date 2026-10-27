namespace Mantenedores.Core.Query;

public sealed record SortSpec(string Column, bool Descending = false);

public sealed record ListRequest
{
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public IReadOnlyList<SortSpec> Sort { get; init; } = [];

    /// <summary>Filtro por columna: texto = contiene; otros tipos = igual.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    /// <summary>Búsqueda general sobre todas las columnas de texto.</summary>
    public string? Search { get; init; }
}

public sealed record PagedResult(
    IReadOnlyList<IDictionary<string, object?>> Rows,
    long Total,
    int Page,
    int PageSize);

public sealed record LookupItem(object? Value, string? Label);
