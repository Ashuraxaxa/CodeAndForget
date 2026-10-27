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
}
