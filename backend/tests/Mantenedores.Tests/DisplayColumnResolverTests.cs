using Mantenedores.Core.Schema;

namespace Mantenedores.Tests;

public class DisplayColumnResolverTests
{
    private static ColumnInfo C(string name, LogicalType type, bool pk = false) =>
        new() { Name = name, NativeType = "x", Type = type, IsPrimaryKey = pk };

    [Fact]
    public void Prefiere_nombres_tipicos()
    {
        var cols = new[] { C("Id", LogicalType.Integer, true), C("Codigo", LogicalType.String), C("Descripcion", LogicalType.String) };
        Assert.Equal("Descripcion", DisplayColumnResolver.Resolve(cols, []));
    }

    [Fact]
    public void Acepta_nombres_compuestos()
    {
        var cols = new[] { C("Id", LogicalType.Integer, true), C("NombreCategoria", LogicalType.String) };
        Assert.Equal("NombreCategoria", DisplayColumnResolver.Resolve(cols, []));
    }

    [Fact]
    public void Usa_la_pk_antes_que_un_texto_largo()
    {
        var cols = new[] { C("id", LogicalType.Integer, true), C("observacion", LogicalType.Text) };
        Assert.Equal("id", DisplayColumnResolver.Resolve(cols, []));
    }

    [Fact]
    public void No_elige_columnas_fk_ni_tipos_no_soportados()
    {
        var cols = new[] { C("id", LogicalType.Integer, true), C("geo", LogicalType.Unsupported), C("tipo_id", LogicalType.String), C("rut", LogicalType.String) };
        Assert.Equal("rut", DisplayColumnResolver.Resolve(cols, ["tipo_id"]));
    }
}
