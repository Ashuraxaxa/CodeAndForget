namespace Mantenedores.Core.Schema;

public sealed record ColumnInfo
{
    public required string Name { get; init; }
    public required string NativeType { get; init; }

    /// <summary>Esquema del tipo nativo (solo tipos definidos por el usuario, p. ej. enums de Postgres).</summary>
    public string? NativeTypeSchema { get; init; }

    public LogicalType Type { get; init; }
    public int Ordinal { get; init; }
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsComputed { get; init; }
    public bool IsRowVersion { get; init; }
    public bool HasDefault { get; init; }
    public bool HasTimeZone { get; init; }
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public IReadOnlyList<string>? EnumValues { get; init; }

    /// <summary>
    /// Número exacto que puede no caber en <c>decimal</c> de .NET (más de 28 dígitos o sin precisión declarada).
    /// Se lee y se escribe como texto (P-023).
    /// </summary>
    public bool IsLargeNumber { get; init; }

    /// <summary>El motor puede ordenar por esta columna (p. ej. no: xml en SQL Server, json en Postgres). Lo fija el proveedor.</summary>
    public bool Sortable { get; init; } = true;

    /// <summary>El motor puede comparar esta columna por igualdad. Lo fija el proveedor.</summary>
    public bool Filterable { get; init; } = true;

    // ----- Ajustes (overrides, P-011): los aplica OverrideApplier; null/false = automático -----

    public string? Label { get; init; }
    public string? HelpText { get; init; }

    /// <summary>Oculta por ajuste: no se lee ni se escribe (salvo si es PK).</summary>
    public bool Hidden { get; init; }

    public bool ShowInGrid { get; init; } = true;

    private readonly int? _order;

    /// <summary>Orden en grilla y formulario; por defecto, la posición en la tabla.</summary>
    public int Order { get => _order ?? Ordinal; init => _order = value; }

    /// <summary>Solo lectura por ajuste.</summary>
    public bool ForcedReadOnly { get; init; }

    /// <summary>Obligatoria por ajuste: además de no aceptar nulos, un texto vacío cuenta como faltante.</summary>
    public bool ForcedRequired { get; init; }

    public string? Widget { get; init; }
    public string? Pattern { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }

    /// <summary>Lista fija de valores permitidos (ajuste).</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Se puede leer de forma segura (los tipos no soportados y binarios se omiten de los SELECT), y no está oculta.</summary>
    public bool IsSelectable => Type is not (LogicalType.Unsupported or LogicalType.Binary) && !(Hidden && !IsPrimaryKey);

    /// <summary>Se ofrece ordenar por ella (P-022).</summary>
    public bool IsSortable => IsSelectable && Sortable;

    /// <summary>Se ofrece filtrar por ella: "contiene" en textos, igualdad en el resto (P-022).</summary>
    public bool IsFilterable => IsSelectable && (Filterable || Type is LogicalType.String or LogicalType.Text);

    /// <summary>La UI no permite escribir esta columna.</summary>
    public bool IsReadOnly => IsIdentity || IsComputed || IsRowVersion || ForcedReadOnly || !IsSelectable;

    /// <summary>Obligatoria al crear: no acepta nulos, no tiene default y no la genera la BD.</summary>
    public bool IsRequiredOnInsert => !IsNullable && !HasDefault && !IsReadOnly;
}
