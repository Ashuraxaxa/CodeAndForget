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

    /// <summary>Se puede leer de forma segura (los tipos no soportados y binarios se omiten de los SELECT).</summary>
    public bool IsSelectable => Type is not (LogicalType.Unsupported or LogicalType.Binary);

    /// <summary>La UI no permite escribir esta columna.</summary>
    public bool IsReadOnly => IsIdentity || IsComputed || IsRowVersion || !IsSelectable;

    /// <summary>Obligatoria al crear: no acepta nulos, no tiene default y no la genera la BD.</summary>
    public bool IsRequiredOnInsert => !IsNullable && !HasDefault && !IsReadOnly;
}
