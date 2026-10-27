namespace Mantenedores.Core.Schema;

public enum TableKind
{
    Table,
    View
}

/// <summary>Restricción UNIQUE: su nombre permite traducir los errores del motor a columnas (P-003).</summary>
public sealed record UniqueKeyInfo(string Name, IReadOnlyList<string> Columns);

public sealed record TableInfo
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public TableKind Kind { get; init; }
    public required IReadOnlyList<ColumnInfo> Columns { get; init; }
    public IReadOnlyList<string> PrimaryKey { get; init; } = [];

    /// <summary>Nombre de la restricción de PK (para traducir errores de duplicado).</summary>
    public string? PrimaryKeyName { get; init; }

    public IReadOnlyList<UniqueKeyInfo> UniqueKeys { get; init; } = [];

    /// <summary>FK salientes: columnas de esta tabla que apuntan a otra.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; } = [];

    /// <summary>FK entrantes: tablas hijas que apuntan a esta (base del maestro-detalle).</summary>
    public IReadOnlyList<ForeignKeyInfo> ReferencedBy { get; init; } = [];

    /// <summary>Columna que se muestra cuando otra tabla referencia a esta (combos de FK).</summary>
    public string? DisplayColumn { get; init; }

    // ----- Ajustes (overrides, P-011) -----

    public string? Label { get; init; }

    /// <summary>No se muestra en el menú.</summary>
    public bool Hidden { get; init; }

    /// <summary>De consulta por ajuste.</summary>
    public bool ForcedReadOnly { get; init; }

    public string? Group { get; init; }

    /// <summary>Orden por defecto: "-fecha,nombre".</summary>
    public string? DefaultSort { get; init; }

    /// <summary>La PK no está declarada en la BD: viene de un ajuste.</summary>
    public bool HasVirtualPrimaryKey { get; init; }

    public string FullName => $"{Schema}.{Name}";
    public bool HasPrimaryKey => PrimaryKey.Count > 0;

    /// <summary>Se editan tablas con PK (declarada o virtual). Vistas, tablas sin PK y las marcadas de consulta quedan de solo lectura.</summary>
    public bool IsEditable => Kind == TableKind.Table && HasPrimaryKey && !ForcedReadOnly;

    public ColumnInfo? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public ForeignKeyInfo? ForeignKeyFor(string column) =>
        ForeignKeys.FirstOrDefault(fk => fk.IsSingleColumn &&
            string.Equals(fk.Columns[0], column, StringComparison.OrdinalIgnoreCase));
}
