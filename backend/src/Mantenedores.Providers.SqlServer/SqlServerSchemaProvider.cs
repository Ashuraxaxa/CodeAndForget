using System.Data.Common;
using Dapper;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.SqlServer;

/// <summary>Lee tablas, columnas y restricciones desde las vistas de catálogo sys.*.</summary>
public sealed class SqlServerSchemaProvider : ISchemaProvider
{
    private const string ObjectFilter = "o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND o.name <> 'sysdiagrams'";

    private const string TablesSql = $"""
        SELECT s.name AS SchemaName, o.name AS TableName, RTRIM(o.type) AS Kind
        FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE {ObjectFilter}
        ORDER BY s.name, o.name
        """;

    // Para tipos alias (CREATE TYPE ... FROM nvarchar(50)) se usa el tipo de sistema base.
    private const string ColumnsSql = $"""
        SELECT s.name AS SchemaName, o.name AS TableName, c.name AS ColumnName, c.column_id AS Ordinal,
               CASE WHEN t.is_user_defined = 0 THEN t.name ELSE TYPE_NAME(c.system_type_id) END AS TypeName,
               CAST(c.max_length AS int) AS MaxLengthBytes,
               CAST(c.precision AS int) AS NumericPrecision,
               CAST(c.scale AS int) AS NumericScale,
               c.is_nullable AS IsNullable,
               c.is_identity AS IsIdentity,
               c.is_computed AS IsComputed,
               CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit) AS HasDefault
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE {ObjectFilter}
        ORDER BY s.name, o.name, c.column_id
        """;

    private const string UniqueKeysSql = """
        SELECT kc.name AS ConstraintName, kc.type AS ConstraintType,
               s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, CAST(ic.key_ordinal AS int) AS Position
        FROM sys.key_constraints kc
        JOIN sys.tables t ON t.object_id = kc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.key_ordinal > 0 AND t.is_ms_shipped = 0
        ORDER BY s.name, t.name, kc.name, ic.key_ordinal
        """;

    private const string ForeignKeysSql = """
        SELECT fk.name AS ConstraintName, 'F' AS ConstraintType,
               s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, fkc.constraint_column_id AS Position,
               rs.name AS RefSchema, rt.name AS RefTable, rc.name AS RefColumn
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, fk.name, fkc.constraint_column_id
        """;

    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct)
    {
        var tables = await connection.QueryAsync<TableRow>(new CommandDefinition(TablesSql, cancellationToken: ct));
        var columns = await connection.QueryAsync<ColumnRow>(new CommandDefinition(ColumnsSql, cancellationToken: ct));
        var uniques = await connection.QueryAsync<KeyRow>(new CommandDefinition(UniqueKeysSql, cancellationToken: ct));
        var fks = await connection.QueryAsync<KeyRow>(new CommandDefinition(ForeignKeysSql, cancellationToken: ct));

        return SchemaAssembler.Assemble(
            tables.Select(t => new RawTable(t.SchemaName, t.TableName, t.Kind == "V" ? TableKind.View : TableKind.Table)),
            columns.Select(c => new RawColumn(c.SchemaName, c.TableName, ToColumn(c))),
            uniques.Concat(fks).Select(ToKey),
            includeSchemas);
    }

    private static ColumnInfo ToColumn(ColumnRow c)
    {
        var length = SqlServerTypeMapper.CharLength(c.TypeName, c.MaxLengthBytes);
        var (type, tz) = SqlServerTypeMapper.Map(c.TypeName, length);
        var isRowVersion = c.TypeName is "timestamp" or "rowversion";
        var isDecimal = c.TypeName is "decimal" or "numeric";
        var isChar = c.TypeName is "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary";

        return new ColumnInfo
        {
            Name = c.ColumnName,
            NativeType = c.TypeName,
            Type = type,
            HasTimeZone = tz,
            Ordinal = c.Ordinal,
            IsNullable = c.IsNullable,
            IsIdentity = c.IsIdentity,
            IsComputed = c.IsComputed,
            IsRowVersion = isRowVersion,
            HasDefault = c.HasDefault || c.IsIdentity || isRowVersion,
            MaxLength = isChar ? length : null,
            Precision = isDecimal ? c.NumericPrecision : null,
            Scale = isDecimal ? c.NumericScale : null
        };
    }

    private static RawKeyColumn ToKey(KeyRow k) => new(
        k.ConstraintName,
        k.ConstraintType.Trim() switch { "PK" => KeyKind.PrimaryKey, "UQ" => KeyKind.Unique, _ => KeyKind.ForeignKey },
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
        public int MaxLengthBytes { get; set; }
        public int NumericPrecision { get; set; }
        public int NumericScale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public bool HasDefault { get; set; }
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
}
