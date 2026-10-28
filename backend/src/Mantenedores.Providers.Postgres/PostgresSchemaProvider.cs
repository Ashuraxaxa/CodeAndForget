using System.Data.Common;
using Dapper;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.Postgres;

/// <summary>Lee tablas, columnas y restricciones desde pg_catalog.</summary>
public sealed class PostgresSchemaProvider : ISchemaProvider
{
    private const string SchemaFilter = """
        n.nspname NOT IN ('pg_catalog', 'information_schema')
        AND n.nspname NOT LIKE 'pg\_toast%' AND n.nspname NOT LIKE 'pg\_temp%'
        """;

    private const string TablesSql = $"""
        SELECT n.nspname AS SchemaName, c.relname AS TableName, c.relkind::text AS Kind
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p', 'v', 'm') AND NOT c.relispartition AND {SchemaFilter}
        ORDER BY 1, 2
        """;

    // Los dominios se resuelven a su tipo base (bt).
    private const string ColumnsSql = $"""
        SELECT n.nspname AS SchemaName, c.relname AS TableName, a.attname AS ColumnName, a.attnum::int AS Ordinal,
               COALESCE(bt.typname, t.typname) AS TypeName,
               CASE WHEN bt.oid IS NULL THEN t.typtype ELSE bt.typtype END::text AS TypeKind,
               CASE WHEN bt.oid IS NULL THEN t.typcategory ELSE bt.typcategory END::text AS TypeCategory,
               CASE WHEN bt.oid IS NULL THEN tn.nspname ELSE btn.nspname END AS TypeSchema,
               NOT a.attnotnull AS IsNullable,
               a.attidentity <> '' AS IsIdentity,
               a.attgenerated <> '' AS IsGenerated,
               pg_get_expr(d.adbin, d.adrelid) AS DefaultExpr,
               CASE WHEN COALESCE(bt.typname, t.typname) IN ('varchar', 'bpchar') AND a.atttypmod > 0
                    THEN a.atttypmod - 4 END AS MaxLength,
               CASE WHEN COALESCE(bt.typname, t.typname) = 'numeric' AND a.atttypmod > 0
                    THEN ((a.atttypmod - 4) >> 16) & 65535 END AS NumericPrecision,
               CASE WHEN COALESCE(bt.typname, t.typname) = 'numeric' AND a.atttypmod > 0
                    THEN (a.atttypmod - 4) & 65535 END AS NumericScale,
               format_type(a.atttypid, a.atttypmod) AS FormatType,
               et.typname AS ElementTypeName, et.typtype::text AS ElementTypeKind,
               et.typcategory::text AS ElementTypeCategory, etn.nspname AS ElementTypeSchema
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_type t ON t.oid = a.atttypid
        JOIN pg_namespace tn ON tn.oid = t.typnamespace
        LEFT JOIN pg_type bt ON t.typtype = 'd' AND bt.oid = t.typbasetype
        LEFT JOIN pg_namespace btn ON btn.oid = bt.typnamespace
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        LEFT JOIN pg_type et ON et.oid = COALESCE(bt.typelem, t.typelem)
              AND COALESCE(bt.typcategory, t.typcategory) = 'A'
        LEFT JOIN pg_namespace etn ON etn.oid = et.typnamespace
        WHERE a.attnum > 0 AND NOT a.attisdropped
          AND c.relkind IN ('r', 'p', 'v', 'm') AND NOT c.relispartition AND {SchemaFilter}
        ORDER BY 1, 2, 4
        """;

    private const string KeysSql = $"""
        SELECT con.conname AS ConstraintName, con.contype::text AS ConstraintType,
               n.nspname AS SchemaName, c.relname AS TableName, a.attname AS ColumnName, k.ord::int AS Position,
               rn.nspname AS RefSchema, rc.relname AS RefTable, ra.attname AS RefColumn
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum
        LEFT JOIN pg_class rc ON rc.oid = con.confrelid
        LEFT JOIN pg_namespace rn ON rn.oid = rc.relnamespace
        LEFT JOIN pg_attribute ra ON ra.attrelid = con.confrelid AND ra.attnum = con.confkey[k.ord]
        WHERE con.contype IN ('p', 'u', 'f') AND {SchemaFilter}
        UNION ALL
        -- Índices únicos que no son restricción (P-031). Sin predicado ni expresiones: esos no valen para la columna entera.
        SELECT ic.relname, 'u', n.nspname, c.relname, a.attname, k.ord::int, NULL, NULL, NULL
        FROM pg_index i
        JOIN pg_class ic ON ic.oid = i.indexrelid
        JOIN pg_class c ON c.oid = i.indrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        CROSS JOIN LATERAL unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
        WHERE i.indisunique AND NOT i.indisprimary AND i.indpred IS NULL AND i.indexprs IS NULL
          AND NOT EXISTS (SELECT 1 FROM pg_constraint pc WHERE pc.conindid = i.indexrelid)
          AND {SchemaFilter}
        ORDER BY 3, 4, 1, 6
        """;

    private const string EnumsSql = """
        SELECT n.nspname AS SchemaName, t.typname AS TypeName, e.enumlabel AS Label
        FROM pg_enum e
        JOIN pg_type t ON t.oid = e.enumtypid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        ORDER BY t.oid, e.enumsortorder
        """;

    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct)
    {
        var tables = await connection.QueryAsync<TableRow>(new CommandDefinition(TablesSql, cancellationToken: ct));
        var columns = await connection.QueryAsync<ColumnRow>(new CommandDefinition(ColumnsSql, cancellationToken: ct));
        var keys = await connection.QueryAsync<KeyRow>(new CommandDefinition(KeysSql, cancellationToken: ct));
        var enums = (await connection.QueryAsync<EnumRow>(new CommandDefinition(EnumsSql, cancellationToken: ct)))
            .GroupBy(e => (e.SchemaName, e.TypeName))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Label).ToList());

        return SchemaAssembler.Assemble(
            tables.Select(t => new RawTable(t.SchemaName, t.TableName, t.Kind is "v" or "m" ? TableKind.View : TableKind.Table)),
            columns.Select(c => new RawColumn(c.SchemaName, c.TableName, ToColumn(c, enums))),
            keys.Select(ToKey),
            includeSchemas);
    }

    private static ColumnInfo ToColumn(ColumnRow c, IReadOnlyDictionary<(string, string), IReadOnlyList<string>> enums)
    {
        var (type, tz) = PostgresTypeMapper.Map(c.TypeName, c.TypeKind, c.TypeCategory);
        // Arreglos (P-018): editables como lista si el elemento es un tipo simple.
        var element = type == LogicalType.Array
            ? PostgresTypeMapper.MapElement(c.ElementTypeName, c.ElementTypeKind, c.ElementTypeCategory)
            : null;
        if (type == LogicalType.Array && element is null) type = LogicalType.Unsupported;
        var isSerial = c.DefaultExpr?.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase) == true;
        return new ColumnInfo
        {
            Name = c.ColumnName,
            NativeType = c.TypeName,
            NativeTypeSchema = c.TypeSchema,
            Type = type,
            HasTimeZone = tz,
            Ordinal = c.Ordinal,
            IsNullable = c.IsNullable,
            IsIdentity = c.IsIdentity || isSerial,
            IsComputed = c.IsGenerated,
            HasDefault = c.DefaultExpr is not null || c.IsIdentity,
            MaxLength = c.MaxLength,
            Precision = c.NumericPrecision,
            Scale = c.NumericScale,
            EnumValues = type == LogicalType.Enum ? enums.GetValueOrDefault((c.TypeSchema, c.TypeName)) : null,
            // numeric sin precisión o con más de 28 dígitos no cabe en decimal de .NET (P-023).
            IsLargeNumber = c.TypeName == "numeric" && (c.NumericPrecision is null || c.NumericPrecision > 28),
            // json y xml no tienen operador de igualdad ni de orden en Postgres (jsonb sí).
            Sortable = c.TypeName is not ("json" or "xml"),
            Filterable = c.TypeName is not ("json" or "xml"),
            ElementType = element,
            TextCast = type == LogicalType.String && PostgresTypeMapper.IsTextCast(c.TypeName, c.TypeKind),
            Srid = PostgresTypeMapper.Srid(c.TypeName, c.FormatType),
            ElementNativeType = element is null ? null : c.ElementTypeName,
            ElementNativeTypeSchema = element is null ? null : c.ElementTypeSchema
        };
    }

    private static RawKeyColumn ToKey(KeyRow k) => new(
        k.ConstraintName,
        k.ConstraintType switch { "p" => KeyKind.PrimaryKey, "u" => KeyKind.Unique, _ => KeyKind.ForeignKey },
        k.SchemaName, k.TableName, k.ColumnName, k.Position,
        k.RefSchema, k.RefTable, k.RefColumn);

    private sealed class TableRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    private sealed class ColumnRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int Ordinal { get; set; }
        public string TypeName { get; set; } = "";
        public string TypeKind { get; set; } = "";
        public string TypeCategory { get; set; } = "";
        public string TypeSchema { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsGenerated { get; set; }
        public string? DefaultExpr { get; set; }
        public int? MaxLength { get; set; }
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public string? FormatType { get; set; }
        public string? ElementTypeName { get; set; }
        public string? ElementTypeKind { get; set; }
        public string? ElementTypeCategory { get; set; }
        public string? ElementTypeSchema { get; set; }
    }

    private sealed class KeyRow
    {
        public string ConstraintName { get; set; } = "";
        public string ConstraintType { get; set; } = "";
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public int Position { get; set; }
        public string? RefSchema { get; set; }
        public string? RefTable { get; set; }
        public string? RefColumn { get; set; }
    }

    private sealed class EnumRow
    {
        public string SchemaName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
