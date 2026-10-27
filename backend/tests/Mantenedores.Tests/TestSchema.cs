using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

/// <summary>Esquema mínimo en memoria: categoria (1) ← producto (N), y una vista.</summary>
public static class TestSchema
{
    public static DatabaseSchema Build()
    {
        var tables = new[]
        {
            new RawTable("ventas", "categoria", TableKind.Table),
            new RawTable("ventas", "producto", TableKind.Table),
            new RawTable("ventas", "v_productos", TableKind.View)
        };

        var columns = new[]
        {
            Col("categoria", "id", LogicalType.Integer, 1, "int4", identity: true),
            Col("categoria", "nombre", LogicalType.String, 2, "varchar", maxLength: 80),
            Col("producto", "id", LogicalType.Integer, 1, "int4", identity: true),
            Col("producto", "codigo", LogicalType.String, 2, "varchar", maxLength: 20),
            Col("producto", "categoria_id", LogicalType.Integer, 3, "int4"),
            Col("producto", "precio", LogicalType.Decimal, 4, "numeric"),
            Col("producto", "foto", LogicalType.Binary, 5, "bytea", nullable: true),
            Col("producto", "notas", LogicalType.Text, 6, "text", nullable: true),
            Col("v_productos", "id", LogicalType.Integer, 1, "int4"),
            Col("v_productos", "nombre", LogicalType.String, 2, "varchar")
        };

        var keys = new[]
        {
            new RawKeyColumn("categoria_pk", KeyKind.PrimaryKey, "ventas", "categoria", "id", 1),
            new RawKeyColumn("producto_pk", KeyKind.PrimaryKey, "ventas", "producto", "id", 1),
            new RawKeyColumn("producto_codigo_uq", KeyKind.Unique, "ventas", "producto", "codigo", 1),
            new RawKeyColumn("producto_categoria_fk", KeyKind.ForeignKey, "ventas", "producto", "categoria_id", 1,
                "ventas", "categoria", "id")
        };

        return SchemaAssembler.Assemble(tables, columns, keys);
    }

    private static RawColumn Col(string table, string name, LogicalType type, int ordinal, string native,
        bool identity = false, bool nullable = false, int? maxLength = null) =>
        new("ventas", table, new ColumnInfo
        {
            Name = name,
            NativeType = native,
            Type = type,
            Ordinal = ordinal,
            IsIdentity = identity,
            HasDefault = identity,
            IsNullable = nullable,
            MaxLength = maxLength
        });
}
