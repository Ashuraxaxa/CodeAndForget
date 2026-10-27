using Mantenedores.Core.Overrides;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.Tests;

/// <summary>Relaciones N:M (P-015): detección de tablas puente y consulta de valores vinculados.</summary>
public class ManyToManyTests
{
    private static DatabaseSchema Build(bool bridgeWithRequiredExtra = false)
    {
        ColumnInfo C(string n, LogicalType t = LogicalType.Integer, bool identity = false, bool nullable = false) =>
            new() { Name = n, NativeType = "int4", Type = t, IsIdentity = identity, HasDefault = identity, IsNullable = nullable };
        RawColumn Col(string table, ColumnInfo c, int ordinal) => new("ventas", table, c with { Ordinal = ordinal });

        var tables = new[]
        {
            new RawTable("ventas", "producto", TableKind.Table),
            new RawTable("ventas", "etiqueta", TableKind.Table),
            new RawTable("ventas", "producto_etiqueta", TableKind.Table)
        };
        var columns = new List<RawColumn>
        {
            Col("producto", C("id", identity: true), 1),
            Col("producto", C("nombre", LogicalType.String), 2),
            Col("etiqueta", C("id", identity: true), 1),
            Col("etiqueta", C("nombre", LogicalType.String), 2),
            Col("producto_etiqueta", C("producto_id"), 1),
            Col("producto_etiqueta", C("etiqueta_id"), 2),
            Col("producto_etiqueta", C("asignada_en", LogicalType.DateTime, nullable: !bridgeWithRequiredExtra), 3)
        };
        var keys = new[]
        {
            new RawKeyColumn("producto_pk", KeyKind.PrimaryKey, "ventas", "producto", "id", 1),
            new RawKeyColumn("etiqueta_pk", KeyKind.PrimaryKey, "ventas", "etiqueta", "id", 1),
            new RawKeyColumn("pe_pk", KeyKind.PrimaryKey, "ventas", "producto_etiqueta", "producto_id", 1),
            new RawKeyColumn("pe_pk", KeyKind.PrimaryKey, "ventas", "producto_etiqueta", "etiqueta_id", 2),
            new RawKeyColumn("pe_producto_fk", KeyKind.ForeignKey, "ventas", "producto_etiqueta", "producto_id", 1, "ventas", "producto", "id"),
            new RawKeyColumn("pe_etiqueta_fk", KeyKind.ForeignKey, "ventas", "producto_etiqueta", "etiqueta_id", 1, "ventas", "etiqueta", "id")
        };
        return OverrideApplier.Apply(SchemaAssembler.Assemble(tables, columns, keys), new Dictionary<(string, string), TableOverride>());
    }

    [Fact]
    public void Detecta_la_tabla_puente_y_la_relacion_desde_ambos_lados()
    {
        var s = Build();
        Assert.True(s.FindTable("ventas", "producto_etiqueta")!.IsBridge);

        var link = Assert.Single(s.FindTable("ventas", "producto")!.ManyToMany);
        Assert.Equal(("producto_etiqueta", "producto_id", "etiqueta_id", "etiqueta", "id"),
            (link.Name, link.LocalColumn, link.ForeignColumn, link.TargetTable, link.TargetColumn));
        Assert.Equal("producto", Assert.Single(s.FindTable("ventas", "etiqueta")!.ManyToMany).TargetTable);

        // El puente no sale como pestaña de hija: lo reemplaza el selector múltiple.
        Assert.Empty(s.FindTable("ventas", "producto")!.Children!);
        Assert.Single(s.FindTable("ventas", "producto")!.ReferencedBy);
    }

    [Fact]
    public void Una_tabla_con_otra_columna_obligatoria_no_es_puente()
    {
        var s = Build(bridgeWithRequiredExtra: true);
        Assert.False(s.FindTable("ventas", "producto_etiqueta")!.IsBridge);
        Assert.Empty(s.FindTable("ventas", "producto")!.ManyToMany);
        Assert.Single(s.FindTable("ventas", "producto")!.Children!); // vuelve a ser una hija normal
    }

    [Fact]
    public void Valores_vinculados_con_su_etiqueta()
    {
        var s = Build();
        var link = s.FindTable("ventas", "producto")!.ManyToMany[0];
        var q = new QueryBuilder(new PostgresDialect()).BuildLinkedValues(
            s.FindTable("ventas", "producto_etiqueta")!, s.FindTable("ventas", "etiqueta")!, link, 7);
        Assert.Equal(
            "SELECT b.\"etiqueta_id\" AS \"value\", t.\"nombre\" AS \"label\" FROM \"ventas\".\"producto_etiqueta\" b " +
            "LEFT JOIN \"ventas\".\"etiqueta\" t ON t.\"id\" = b.\"etiqueta_id\" WHERE b.\"producto_id\" = @p0 ORDER BY t.\"nombre\"",
            q.Sql);
        Assert.Equal(7, q.Parameters["p0"]);
    }
}
