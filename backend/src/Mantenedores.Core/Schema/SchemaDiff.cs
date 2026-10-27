using System.Text.Json.Serialization;

namespace Mantenedores.Core.Schema;

/// <summary>
/// Foto mínima del esquema para comparar (P-013): solo lo que, si cambia, afecta a un mantenedor.
/// Se guarda como JSON en la metadata; por eso es un modelo propio y estable, no <see cref="TableInfo"/>.
/// </summary>
public sealed record SchemaSnapshot(IReadOnlyList<SnapshotTable> Tables)
{
    public static SchemaSnapshot From(DatabaseSchema schema) => new(schema.Tables.Select(t => new SnapshotTable(
        t.Schema, t.Name, t.Kind,
        t.Columns.OrderBy(c => c.Ordinal).Select(c => new SnapshotColumn(c.Name, TypeText(c), c.IsNullable)).ToList(),
        t.HasVirtualPrimaryKey ? [] : t.PrimaryKey,
        t.ForeignKeys.Select(f => new SnapshotForeignKey(f.Name, f.Columns, f.RefSchema, f.RefTable, f.RefColumns)).ToList()))
        .ToList());

    /// <summary>Tipo como se lee en la base: "varchar(20)", "numeric(12,2)".</summary>
    private static string TypeText(ColumnInfo c) =>
        c.MaxLength is { } len ? $"{c.NativeType}({(len < 0 ? "max" : len)})"
        : c.Precision is { } p && c.Type == LogicalType.Decimal ? $"{c.NativeType}({p},{c.Scale ?? 0})"
        : c.NativeType;
}

public sealed record SnapshotTable(
    string Schema, string Name, TableKind Kind,
    IReadOnlyList<SnapshotColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<SnapshotForeignKey> ForeignKeys);

public sealed record SnapshotColumn(string Name, string Type, bool IsNullable);

public sealed record SnapshotForeignKey(
    string Name, IReadOnlyList<string> Columns, string RefSchema, string RefTable, IReadOnlyList<string> RefColumns);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaChangeKind
{
    TableAdded,
    TableRemoved,
    TableKindChanged,
    ColumnAdded,
    ColumnRemoved,
    ColumnTypeChanged,
    ColumnNullabilityChanged,
    PrimaryKeyChanged,
    ForeignKeyAdded,
    ForeignKeyRemoved
}

/// <summary>Un cambio del esquema. <c>Description</c> va en español, lista para mostrar.</summary>
public sealed record SchemaChange(SchemaChangeKind Kind, string Schema, string Table, string? Column, string Description);

/// <summary>Compara la foto de referencia con el esquema leído ahora.</summary>
public static class SchemaDiff
{
    public static IReadOnlyList<SchemaChange> Compare(SchemaSnapshot before, SchemaSnapshot after)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        static string Key(string s, string t) => $"{s}\u0001{t}".ToLowerInvariant();
        var old = before.Tables.ToDictionary(t => Key(t.Schema, t.Name));
        var now = after.Tables.ToDictionary(t => Key(t.Schema, t.Name));
        var changes = new List<SchemaChange>();

        foreach (var t in after.Tables.Where(t => !old.ContainsKey(Key(t.Schema, t.Name))))
            changes.Add(new(SchemaChangeKind.TableAdded, t.Schema, t.Name, null,
                $"{(t.Kind == TableKind.View ? "Vista" : "Tabla")} nueva"));
        foreach (var t in before.Tables.Where(t => !now.ContainsKey(Key(t.Schema, t.Name))))
            changes.Add(new(SchemaChangeKind.TableRemoved, t.Schema, t.Name, null,
                $"{(t.Kind == TableKind.View ? "Vista" : "Tabla")} eliminada"));

        foreach (var a in after.Tables)
        {
            if (!old.TryGetValue(Key(a.Schema, a.Name), out var b)) continue;
            void Add(SchemaChangeKind k, string? col, string d) => changes.Add(new(k, a.Schema, a.Name, col, d));

            if (a.Kind != b.Kind)
                Add(SchemaChangeKind.TableKindChanged, null, a.Kind == TableKind.View ? "Ahora es una vista" : "Ahora es una tabla");

            var oldCols = b.Columns.ToDictionary(c => c.Name, cmp);
            var newCols = a.Columns.ToDictionary(c => c.Name, cmp);
            foreach (var c in a.Columns.Where(c => !oldCols.ContainsKey(c.Name)))
                Add(SchemaChangeKind.ColumnAdded, c.Name, $"Columna nueva ({c.Type}{(c.IsNullable ? "" : ", obligatoria")})");
            foreach (var c in b.Columns.Where(c => !newCols.ContainsKey(c.Name)))
                Add(SchemaChangeKind.ColumnRemoved, c.Name, "Columna eliminada");
            foreach (var c in a.Columns)
            {
                if (!oldCols.TryGetValue(c.Name, out var o)) continue;
                if (!string.Equals(o.Type, c.Type, StringComparison.OrdinalIgnoreCase))
                    Add(SchemaChangeKind.ColumnTypeChanged, c.Name, $"Tipo: {o.Type} → {c.Type}");
                if (o.IsNullable != c.IsNullable)
                    Add(SchemaChangeKind.ColumnNullabilityChanged, c.Name, c.IsNullable ? "Ahora acepta vacíos" : "Ahora es obligatoria");
            }

            if (!a.PrimaryKey.SequenceEqual(b.PrimaryKey, cmp))
                Add(SchemaChangeKind.PrimaryKeyChanged, null,
                    $"Clave primaria: {Cols(b.PrimaryKey)} → {Cols(a.PrimaryKey)}");

            static string Fk(SnapshotForeignKey f) =>
                $"{string.Join(",", f.Columns)}→{f.RefSchema}.{f.RefTable}({string.Join(",", f.RefColumns)})".ToLowerInvariant();
            var oldFks = b.ForeignKeys.Select(Fk).ToHashSet();
            var newFks = a.ForeignKeys.Select(Fk).ToHashSet();
            foreach (var f in a.ForeignKeys.Where(f => !oldFks.Contains(Fk(f))))
                Add(SchemaChangeKind.ForeignKeyAdded, f.Columns.Count == 1 ? f.Columns[0] : null,
                    $"Relación nueva: {string.Join(", ", f.Columns)} → {f.RefTable}");
            foreach (var f in b.ForeignKeys.Where(f => !newFks.Contains(Fk(f))))
                Add(SchemaChangeKind.ForeignKeyRemoved, f.Columns.Count == 1 ? f.Columns[0] : null,
                    $"Relación eliminada: {string.Join(", ", f.Columns)} → {f.RefTable}");
        }

        return changes
            .OrderBy(c => c.Schema, cmp).ThenBy(c => c.Table, cmp).ThenBy(c => c.Kind).ThenBy(c => c.Column, cmp)
            .ToList();
    }

    private static string Cols(IReadOnlyList<string> c) => c.Count == 0 ? "(ninguna)" : string.Join(", ", c);
}

/// <summary>Dónde se guarda la foto de referencia de cada conexión (metadata del servicio).</summary>
public interface ISchemaSnapshotStore
{
    Task<StoredSnapshot?> GetAsync(string connectionId, CancellationToken ct);
    Task SaveAsync(string connectionId, SchemaSnapshot snapshot, string? user, CancellationToken ct);
    Task DeleteAsync(string connectionId, CancellationToken ct);
}

public sealed record StoredSnapshot(SchemaSnapshot Snapshot, DateTimeOffset TakenAt, string? TakenBy);

/// <summary>Sin fotos guardadas: nunca hay diferencias (pruebas y usos sin metadata).</summary>
public sealed class NoSnapshotStore : ISchemaSnapshotStore
{
    public Task<StoredSnapshot?> GetAsync(string connectionId, CancellationToken ct) => Task.FromResult<StoredSnapshot?>(null);
    public Task SaveAsync(string connectionId, SchemaSnapshot snapshot, string? user, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(string connectionId, CancellationToken ct) => Task.CompletedTask;
}
