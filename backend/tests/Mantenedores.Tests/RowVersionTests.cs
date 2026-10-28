using Mantenedores.Core.Concurrency;

namespace Mantenedores.Tests;

/// <summary>Versión de fila para la concurrencia optimista (P-046, D-069).</summary>
public sealed class RowVersionTests
{
    private static readonly Mantenedores.Core.Schema.TableInfo Producto = TestSchema.Build().FindTable("ventas", "producto")!;

    [Fact]
    public void Es_estable_y_no_depende_del_orden_ni_de_etiquetas_ni_mayusculas()
    {
        var a = new Dictionary<string, object?> { ["id"] = 1, ["codigo"] = "A", ["precio"] = 10.5m, ["categoria_id__label"] = "X" };
        var b = new Dictionary<string, object?> { ["PRECIO"] = 10.5m, ["Codigo"] = "A", ["ID"] = 1, ["categoria_id__label"] = "Otra" };
        Assert.Equal(RowVersion.Of(Producto, a), RowVersion.Of(Producto, b));
    }

    [Fact]
    public void Cambia_con_cualquier_valor_incluido_el_tamano_de_un_archivo_o_un_nulo()
    {
        var baseRow = new Dictionary<string, object?> { ["id"] = 1, ["codigo"] = "A", ["notas"] = null, ["foto__size"] = 10L };
        var v = RowVersion.Of(Producto, baseRow);
        Assert.NotEqual(v, RowVersion.Of(Producto, new Dictionary<string, object?>(baseRow) { ["codigo"] = "B" }));
        Assert.NotEqual(v, RowVersion.Of(Producto, new Dictionary<string, object?>(baseRow) { ["notas"] = "" }));
        Assert.NotEqual(v, RowVersion.Of(Producto, new Dictionary<string, object?>(baseRow) { ["foto__size"] = 11L }));
    }

    [Fact]
    public void Se_agrega_a_la_fila_solo_si_la_tabla_tiene_clave()
    {
        var row = RowVersion.Stamp(Producto, new Dictionary<string, object?> { ["id"] = 1 });
        Assert.True(row.ContainsKey(RowVersion.Field));
        var vista = TestSchema.Build().FindTable("ventas", "v_productos")!;
        Assert.False(RowVersion.Stamp(vista, new Dictionary<string, object?> { ["id"] = 1 }).ContainsKey(RowVersion.Field));
    }
}
