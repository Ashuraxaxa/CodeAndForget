using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

public class SchemaAssemblerTests
{
    [Fact]
    public void Marca_pk_y_arma_relaciones_en_ambos_sentidos()
    {
        var schema = TestSchema.Build();
        var producto = schema.FindTable("ventas", "producto")!;
        var categoria = schema.FindTable("VENTAS", "Categoria")!; // búsqueda sin distinguir mayúsculas

        Assert.Equal(["id"], producto.PrimaryKey);
        Assert.True(producto.FindColumn("id")!.IsPrimaryKey);
        Assert.Single(producto.UniqueKeys);

        var fk = Assert.Single(producto.ForeignKeys);
        Assert.Equal("categoria", fk.RefTable);
        Assert.Equal(["categoria_id"], fk.Columns);

        var incoming = Assert.Single(categoria.ReferencedBy);
        Assert.Equal("producto", incoming.Table);
    }

    [Fact]
    public void Vistas_y_tablas_sin_pk_no_son_editables()
    {
        var schema = TestSchema.Build();
        Assert.False(schema.FindTable("ventas", "v_productos")!.IsEditable);
        Assert.True(schema.FindTable("ventas", "producto")!.IsEditable);
    }

    [Fact]
    public void Filtra_por_esquemas_incluidos()
    {
        var tables = new[] { new RawTable("a", "t1", TableKind.Table), new RawTable("b", "t2", TableKind.Table) };
        var schema = SchemaAssembler.Assemble(tables, [], [], ["b"]);
        Assert.Equal("t2", Assert.Single(schema.Tables).Name);
    }

    [Fact]
    public void El_hash_cambia_si_cambia_el_esquema()
    {
        var a = SchemaAssembler.Assemble([new RawTable("s", "t", TableKind.Table)], [], []);
        var b = SchemaAssembler.Assemble([new RawTable("s", "t2", TableKind.Table)], [], []);
        Assert.NotEqual(a.Hash, b.Hash);
    }
}
