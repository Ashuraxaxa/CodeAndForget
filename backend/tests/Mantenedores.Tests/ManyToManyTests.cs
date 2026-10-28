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

public class ManyToManyDisplayAndTreeTests
{
    private static DatabaseSchema Build(Dictionary<(string, string), TableOverride> overrides)
    {
        ColumnInfo C(string n, LogicalType t = LogicalType.Integer, bool identity = false, bool nullable = false) =>
            new() { Name = n, NativeType = "int4", Type = t, IsIdentity = identity, HasDefault = identity, IsNullable = nullable };
        RawColumn Col(string table, ColumnInfo c, int ordinal) => new("ventas", table, c with { Ordinal = ordinal });
        var tables = new[]
        {
            new RawTable("ventas", "producto", TableKind.Table), new RawTable("ventas", "etiqueta", TableKind.Table),
            new RawTable("ventas", "producto_etiqueta", TableKind.Table), new RawTable("ventas", "empleado", TableKind.Table)
        };
        var columns = new List<RawColumn>
        {
            Col("producto", C("id", identity: true), 1), Col("producto", C("nombre", LogicalType.String), 2),
            Col("etiqueta", C("id", identity: true), 1), Col("etiqueta", C("nombre", LogicalType.String), 2),
            Col("producto_etiqueta", C("producto_id"), 1), Col("producto_etiqueta", C("etiqueta_id"), 2),
            Col("producto_etiqueta", C("cantidad", nullable: true), 3),
            Col("empleado", C("id", identity: true), 1), Col("empleado", C("jefe_id", nullable: true), 2),
            Col("empleado", C("mentor_id", nullable: true), 3)
        };
        var keys = new[]
        {
            new RawKeyColumn("producto_pk", KeyKind.PrimaryKey, "ventas", "producto", "id", 1),
            new RawKeyColumn("etiqueta_pk", KeyKind.PrimaryKey, "ventas", "etiqueta", "id", 1),
            new RawKeyColumn("pe_pk", KeyKind.PrimaryKey, "ventas", "producto_etiqueta", "producto_id", 1),
            new RawKeyColumn("pe_pk", KeyKind.PrimaryKey, "ventas", "producto_etiqueta", "etiqueta_id", 2),
            new RawKeyColumn("pe_producto_fk", KeyKind.ForeignKey, "ventas", "producto_etiqueta", "producto_id", 1, "ventas", "producto", "id"),
            new RawKeyColumn("pe_etiqueta_fk", KeyKind.ForeignKey, "ventas", "producto_etiqueta", "etiqueta_id", 1, "ventas", "etiqueta", "id"),
            new RawKeyColumn("empleado_pk", KeyKind.PrimaryKey, "ventas", "empleado", "id", 1),
            new RawKeyColumn("fk_jefe", KeyKind.ForeignKey, "ventas", "empleado", "jefe_id", 1, "ventas", "empleado", "id"),
            new RawKeyColumn("fk_mentor", KeyKind.ForeignKey, "ventas", "empleado", "mentor_id", 1, "ventas", "empleado", "id")
        };
        return OverrideApplier.Apply(SchemaAssembler.Assemble(tables, columns, keys), overrides);
    }

    [Fact]
    public void Una_relacion_nm_puede_editarse_en_pestana_o_no_mostrarse()
    {
        var auto = Build([]).FindTable("ventas", "producto")!;
        Assert.Equal(LinkDisplay.Selector, Assert.Single(auto.ManyToMany).Display);
        Assert.Empty(auto.Children!);

        var tab = Build(new() { [("ventas", "producto")] = new TableOverride { Links = new Dictionary<string, string> { ["producto_etiqueta"] = "tab" } } })
            .FindTable("ventas", "producto")!;
        Assert.Equal(LinkDisplay.Tab, tab.ManyToMany[0].Display);
        Assert.Equal("pe_producto_fk", Assert.Single(tab.Children!).Name); // el puente, con su propio formulario
        // Del otro lado sigue el selector.
        Assert.Equal(LinkDisplay.Selector, Build(new() { [("ventas", "producto")] = new TableOverride { Links = new Dictionary<string, string> { ["producto_etiqueta"] = "tab" } } })
            .FindTable("ventas", "etiqueta")!.ManyToMany[0].Display);

        var none = Build(new() { [("ventas", "producto")] = new TableOverride { Links = new Dictionary<string, string> { ["producto_etiqueta"] = "none" } } })
            .FindTable("ventas", "producto")!;
        Assert.Equal(LinkDisplay.None, none.ManyToMany[0].Display);
        Assert.Empty(none.Children!);
    }

    [Fact]
    public void El_arbol_usa_la_autorreferencia_elegida()
    {
        Assert.Equal("fk_jefe", Build([]).FindTable("ventas", "empleado")!.SelfReference!.Name);
        var mentor = Build(new() { [("ventas", "empleado")] = new TableOverride { TreeParent = "fk_mentor" } });
        Assert.Equal("fk_mentor", mentor.FindTable("ventas", "empleado")!.SelfReference!.Name);

        var raw = Build([]);
        var ex = Assert.Throws<Mantenedores.Core.Errors.ValidationException>(() =>
            OverrideApplier.Validate(raw, "ventas", "empleado", new TableOverride { TreeParent = "no_existe", Links = new Dictionary<string, string> { ["x"] = "raro" } }));
        Assert.Contains("treeParent", ex.Errors.Keys);
        Assert.Contains("links", ex.Errors.Keys);
    }
}
