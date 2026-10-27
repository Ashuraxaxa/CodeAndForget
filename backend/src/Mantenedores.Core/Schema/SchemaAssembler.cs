using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mantenedores.Core.Schema;

/// <summary>Fila cruda de una tabla o vista, tal como la entrega un proveedor.</summary>
public sealed record RawTable(string Schema, string Name, TableKind Kind);

/// <summary>Fila cruda de una columna; el proveedor ya resolvió el tipo lógico.</summary>
public sealed record RawColumn(string Schema, string Table, ColumnInfo Column);

public enum KeyKind
{
    PrimaryKey,
    Unique,
    ForeignKey
}

/// <summary>Una columna de una restricción (PK, UNIQUE o FK).</summary>
public sealed record RawKeyColumn(
    string Name,
    KeyKind Kind,
    string Schema,
    string Table,
    string Column,
    int Position,
    string? RefSchema = null,
    string? RefTable = null,
    string? RefColumn = null);

/// <summary>
/// Une las filas crudas de cualquier proveedor en un <see cref="DatabaseSchema"/>:
/// marca PK, arma FK salientes y entrantes, y elige la columna a mostrar.
/// Así cada proveedor solo se ocupa de sus consultas de catálogo.
/// </summary>
public static class SchemaAssembler
{
    public static DatabaseSchema Assemble(
        IEnumerable<RawTable> tables,
        IEnumerable<RawColumn> columns,
        IEnumerable<RawKeyColumn> keys,
        IReadOnlyCollection<string>? includeSchemas = null)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool Included(string schema) => includeSchemas is null || includeSchemas.Count == 0 || includeSchemas.Contains(schema, cmp);

        static string Key(string schema, string table) => $"{schema}\u0001{table}".ToLowerInvariant();

        var columnsByTable = columns
            .Where(c => Included(c.Schema))
            .GroupBy(c => Key(c.Schema, c.Table))
            .ToDictionary(g => g.Key, g => g.Select(c => c.Column).OrderBy(c => c.Ordinal).ToList());

        var keysByTable = keys
            .Where(k => Included(k.Schema))
            .GroupBy(k => Key(k.Schema, k.Table))
            .ToDictionary(g => g.Key, g => g.ToList());

        var built = new List<TableInfo>();
        foreach (var t in tables.Where(t => Included(t.Schema)))
        {
            var k = Key(t.Schema, t.Name);
            var cols = columnsByTable.GetValueOrDefault(k) ?? [];
            var tableKeys = keysByTable.GetValueOrDefault(k) ?? [];

            var pkRows = tableKeys.Where(x => x.Kind == KeyKind.PrimaryKey).OrderBy(x => x.Position).ToList();
            var pk = pkRows.Select(x => x.Column).ToList();

            var uniques = tableKeys.Where(x => x.Kind == KeyKind.Unique)
                .GroupBy(x => x.Name)
                .Select(g => new UniqueKeyInfo(g.Key, g.OrderBy(x => x.Position).Select(x => x.Column).ToList()))
                .ToList();

            var fks = tableKeys.Where(x => x.Kind == KeyKind.ForeignKey)
                .GroupBy(x => x.Name)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.Position).ToList();
                    return new ForeignKeyInfo
                    {
                        Name = g.Key,
                        Schema = t.Schema,
                        Table = t.Name,
                        Columns = ordered.Select(x => x.Column).ToList(),
                        RefSchema = ordered[0].RefSchema!,
                        RefTable = ordered[0].RefTable!,
                        RefColumns = ordered.Select(x => x.RefColumn!).ToList()
                    };
                })
                .Where(fk => Included(fk.RefSchema))
                .ToList();

            var finalCols = cols.Select(c => c with { IsPrimaryKey = pk.Contains(c.Name, cmp) }).ToList();
            var fkColumns = fks.SelectMany(f => f.Columns).ToHashSet(cmp);

            built.Add(new TableInfo
            {
                Schema = t.Schema,
                Name = t.Name,
                Kind = t.Kind,
                Columns = finalCols,
                PrimaryKey = pk,
                PrimaryKeyName = pkRows.FirstOrDefault()?.Name,
                UniqueKeys = uniques,
                ForeignKeys = fks,
                DisplayColumn = DisplayColumnResolver.Resolve(finalCols, fkColumns)
            });
        }

        // FK entrantes: se calculan después, cuando ya existen todas las tablas.
        var incoming = built.SelectMany(t => t.ForeignKeys)
            .GroupBy(fk => Key(fk.RefSchema, fk.RefTable))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ForeignKeyInfo>)g.ToList());

        var result = built
            .Select(t => t with { ReferencedBy = incoming.GetValueOrDefault(Key(t.Schema, t.Name)) ?? [] })
            .OrderBy(t => t.Schema, cmp).ThenBy(t => t.Name, cmp)
            .ToList();

        return new DatabaseSchema { Tables = result, Hash = ComputeHash(result) };
    }

    private static string ComputeHash(IReadOnlyList<TableInfo> tables)
    {
        var json = JsonSerializer.Serialize(tables);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
    }
}
