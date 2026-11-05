namespace Mantenedores.Core.Query;

/// <summary>Qué calcula una tarjeta de dashboard (D-111).</summary>
public enum AggregateFunction
{
    /// <summary>Filas (no lleva columna).</summary>
    Count,
    /// <summary>Valores distintos de una columna.</summary>
    CountDistinct,
    Sum,
    Avg,
    Min,
    Max
}

/// <summary>Cómo se agrupa una columna de fecha: cada valor cae al inicio de su día, semana (lunes), mes, trimestre o año.</summary>
public enum DateBucket
{
    Day,
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>Orden de los grupos: por su valor (series de tiempo) o por el resultado (top N).</summary>
public enum AggregateOrder
{
    Key,
    ValueDesc,
    ValueAsc
}

/// <summary>Una agrupación: por columna, por su FK (con el texto de la tabla referenciada) o por fecha.</summary>
/// <param name="Bucket">Solo para columnas de fecha; sin él, se agrupa por el valor exacto.</param>
public sealed record AggregateGroup(string Column, DateBucket? Bucket = null);

/// <summary>
/// Consulta de resumen (P-127, D-111): una métrica, hasta dos agrupaciones (la segunda arma la tabla dinámica) y los
/// mismos filtros de la grilla. Tablas y columnas se validan contra la metadata; los valores van como parámetros.
/// </summary>
public sealed record AggregateRequest
{
    /// <summary>Tope de grupos por defecto y máximo (la tabla dinámica cuenta celdas).</summary>
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;

    public AggregateFunction Function { get; init; } = AggregateFunction.Count;

    /// <summary>Columna de la métrica; no se usa con <see cref="AggregateFunction.Count"/>.</summary>
    public string? Column { get; init; }

    /// <summary>Cero, una o dos agrupaciones.</summary>
    public IReadOnlyList<AggregateGroup> GroupBy { get; init; } = [];

    public AggregateOrder Order { get; init; } = AggregateOrder.Key;

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Filtros de la grilla: contiene en textos, igual en el resto (y por el texto de la FK).</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    /// <summary>Igualdad exacta por columna.</summary>
    public IReadOnlyDictionary<string, string> Exact { get; init; } = new Dictionary<string, string>();

    /// <summary>Rango de fechas del dashboard: <see cref="DateColumn"/> &gt;= From y &lt; To (cualquiera puede faltar).</summary>
    public string? DateColumn { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
}

/// <summary>Un grupo del resultado. Key2/Label2 solo con dos agrupaciones; Label es el texto de la FK si la hay.</summary>
public sealed record AggregateRow(object? Key, string? Label, object? Key2, string? Label2, object? Value);

/// <param name="Truncated">Había más grupos que el tope: la UI lo avisa ("se muestran los primeros 50").</param>
public sealed record AggregateResult(IReadOnlyList<AggregateRow> Rows, bool Truncated);
