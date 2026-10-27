namespace Mantenedores.Core.Schema;

public enum TableKind
{
    Table,
    View
}

public sealed record TableInfo
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public TableKind Kind { get; init; }
    public required IReadOnlyList<ColumnInfo> Columns { get; init; }
    public IReadOnlyList<string> PrimaryKey { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> UniqueKeys { get; init; } = [];

    /// <summary>FK salientes: columnas de esta tabla que apuntan a otra.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; } = [];

    /// <summary>FK entrantes: tablas hijas que apuntan a esta (base del maestro-detalle).</summary>
    public IReadOnlyList<ForeignKeyInfo> ReferencedBy { get; init; } = [];

    /// <summary>Columna que se muestra cuando otra tabla referencia a esta (combos de FK).</summary>
    public string? DisplayColumn { get; init; }

    public string FullName => $"{Schema}.{Name}";
    public bool HasPrimaryKey => PrimaryKey.Count > 0;

    /// <summary>En Fase 1 solo se editan tablas con PK. Vistas y tablas sin PK quedan de solo lectura.</summary>
    public bool IsEditable => Kind == TableKind.Table && HasPrimaryKey;

    public ColumnInfo? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public ForeignKeyInfo? ForeignKeyFor(string column) =>
        ForeignKeys.FirstOrDefault(fk => fk.IsSingleColumn &&
            string.Equals(fk.Columns[0], column, StringComparison.OrdinalIgnoreCase));
}
