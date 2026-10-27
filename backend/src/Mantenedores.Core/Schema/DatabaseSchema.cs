namespace Mantenedores.Core.Schema;

public sealed record DatabaseSchema
{
    public required IReadOnlyList<TableInfo> Tables { get; init; }

    /// <summary>Huella del esquema; permite detectar cambios al volver a leerlo.</summary>
    public required string Hash { get; init; }

    public DateTimeOffset ReadAt { get; init; } = DateTimeOffset.UtcNow;

    public TableInfo? FindTable(string schema, string name) =>
        Tables.FirstOrDefault(t =>
            string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
