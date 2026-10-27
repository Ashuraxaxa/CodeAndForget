using Mantenedores.Core.Errors;

namespace Mantenedores.Tests;

public class DbErrorTranslatorTests
{
    private readonly Core.Schema.DatabaseSchema _schema = TestSchema.Build();
    private Core.Schema.TableInfo Producto => _schema.FindTable("ventas", "producto")!;
    private Core.Schema.TableInfo Categoria => _schema.FindTable("ventas", "categoria")!;

    [Fact]
    public void Unico_indica_la_columna()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.Unique, "producto_codigo_uq"), Producto, DbOperation.Insert)!;
        Assert.Equal("Ya existe un registro con ese valor en codigo.", ex.Message);
        Assert.Equal("ya existe un registro con este valor", ex.Errors["codigo"]);
    }

    [Fact]
    public void Pk_duplicada_indica_las_columnas_de_la_pk()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.Unique, "PRODUCTO_PK"), Producto, DbOperation.Insert)!;
        Assert.Contains("id", ex.Errors.Keys);
    }

    [Fact]
    public void Fk_al_insertar_indica_la_tabla_referenciada()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.ForeignKey, "producto_categoria_fk"), Producto, DbOperation.Insert)!;
        Assert.Equal("El valor de categoria_id no existe en categoria.", ex.Message);
        Assert.Equal("no existe en categoria", ex.Errors["categoria_id"]);
    }

    [Fact]
    public void Fk_al_borrar_indica_quien_lo_usa()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.ForeignKey, "producto_categoria_fk"), Categoria, DbOperation.Delete)!;
        Assert.Equal("No se puede eliminar: lo usan registros de producto.", ex.Message);
    }

    [Theory]
    [InlineData(DbOperation.Insert, "producto_categoria_fk", DbErrorKind.ForeignKey)]
    [InlineData(DbOperation.Insert, "CK_precio_positivo", DbErrorKind.Check)]
    public void Codigo_ambiguo_fk_o_check_se_resuelve_con_la_metadata(DbOperation op, string constraint, DbErrorKind expected)
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.ForeignKeyOrCheck, constraint), Producto, op)!;
        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public void Nulo_indica_la_columna()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.NotNull, Column: "PRECIO"), Producto, DbOperation.Insert)!;
        Assert.Equal("precio es obligatorio.", ex.Message);
        Assert.Contains("precio", ex.Errors.Keys);
    }

    [Fact]
    public void Restriccion_desconocida_da_un_mensaje_generico()
    {
        var ex = DbErrorTranslator.Translate(new(DbErrorKind.Unique, "otro_indice"), Producto, DbOperation.Insert)!;
        Assert.Equal("Ya existe un registro con esos valores.", ex.Message);
        Assert.Empty(ex.Errors);
    }

    [Fact]
    public void Error_no_clasificado_no_se_traduce() =>
        Assert.Null(DbErrorTranslator.Translate(DbErrorInfo.Unknown, Producto, DbOperation.Insert));
}
