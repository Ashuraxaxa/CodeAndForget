using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;

namespace Mantenedores.Tests;

/// <summary>Permisos por conexión, tabla y columna con herencia y unión de perfiles (D-062, D-063).</summary>
public sealed class UserAccessTests
{
    private static SecurityProfile Profile(string id, string[]? roles = null, string[]? users = null,
        Dictionary<string, ConnectionRule>? connections = null, string[]? app = null) => new()
    {
        Id = id, Name = id, Roles = roles ?? [], Users = users ?? [], AppPermissions = app ?? [],
        Connections = connections ?? []
    };

    private static ConnectionRule Conn(string[] ops, Dictionary<string, TableRule>? tables = null) =>
        new() { Operations = ops, Tables = tables ?? [] };

    private static TableRule Table(string[] ops, Dictionary<string, string[]>? columns = null) => new()
    {
        Operations = ops,
        Columns = (columns ?? []).ToDictionary(kv => kv.Key, kv => new ColumnRule { Operations = kv.Value })
    };

    [Fact]
    public void Sin_perfiles_todo_queda_abierto()
    {
        var access = UserAccess.For("ana", [], isAdmin: false, []);
        Assert.True(access.IsOpen);
        Assert.True(access.Unrestricted);
        Assert.Equal(DataOperation.All, access.Table("demo", "ventas", "producto"));
    }

    [Fact]
    public void Con_perfiles_quien_no_tiene_ninguno_no_ve_nada_salvo_el_administrador()
    {
        var profiles = new[] { Profile("ventas", roles: ["ventas"], connections: new() { ["demo"] = Conn(["view"]) }) };

        var nadie = UserAccess.For("ana", ["otro"], isAdmin: false, profiles);
        Assert.False(nadie.IsOpen);
        Assert.False(nadie.CanSeeConnection("demo"));
        Assert.Empty(nadie.Restrict("demo", TestSchema.Build()).Tables);

        var admin = UserAccess.For("root", [], isAdmin: true, profiles);
        Assert.True(admin.Unrestricted);
        Assert.Equal(3, admin.Restrict("demo", TestSchema.Build()).Tables.Count);
    }

    [Fact]
    public void La_tabla_reemplaza_a_la_conexion_y_la_columna_a_la_tabla()
    {
        var profiles = new[]
        {
            Profile("p", users: ["ana"], connections: new()
            {
                ["demo"] = Conn(["view", "create", "update"], new()
                {
                    ["ventas.producto"] = Table(["view", "update"], new() { ["precio"] = ["view"], ["notas"] = [] })
                })
            })
        };
        var a = UserAccess.For("ANA", [], false, profiles);

        Assert.Equal(DataOperation.View | DataOperation.Create | DataOperation.Update, a.Table("demo", "ventas", "categoria"));
        Assert.Equal(DataOperation.View | DataOperation.Update, a.Table("demo", "VENTAS", "Producto"));
        Assert.Equal(DataOperation.View | DataOperation.Update, a.Column("demo", "ventas", "producto", "codigo")); // sigue a la tabla
        Assert.Equal(DataOperation.View, a.Column("demo", "ventas", "producto", "precio"));
        Assert.Equal(DataOperation.None, a.Column("demo", "ventas", "producto", "notas"));
        Assert.Equal(DataOperation.None, a.Table("otra", "ventas", "producto")); // otra conexión
    }

    [Fact]
    public void Los_perfiles_se_suman_y_el_comodin_vale_para_todas_las_conexiones()
    {
        var profiles = new[]
        {
            Profile("lectura", roles: ["lector"], connections: new() { ["*"] = Conn(["view", "export"]) }),
            Profile("altas", roles: ["digitador"], connections: new() { ["demo"] = Conn([], new() { ["ventas.categoria"] = Table(["view", "create"]) }) },
                app: ["ConfigureTables"])
        };
        var a = UserAccess.For("ana", ["lector", "digitador"], false, profiles);

        Assert.Equal(DataOperation.View | DataOperation.Export | DataOperation.Create, a.Table("demo", "ventas", "categoria"));
        Assert.Equal(DataOperation.View | DataOperation.Export, a.Table("demo", "ventas", "producto"));
        Assert.Equal(DataOperation.View | DataOperation.Export, a.Table("cualquiera", "x", "y"));
        Assert.Contains("ConfigureTables", a.AppPermissions);
        Assert.Equal(["altas", "lectura"], a.ProfileNames.Order());
        Assert.True(UserAccess.For("x", ["digitador"], false, profiles).CanSeeConnection("demo")); // solo por una tabla
    }

    [Fact]
    public void Restrict_quita_tablas_oculta_columnas_y_deja_de_solo_lectura_lo_que_no_se_edita()
    {
        var profiles = new[]
        {
            Profile("p", users: ["ana"], connections: new()
            {
                ["demo"] = Conn([], new()
                {
                    ["ventas.producto"] = Table(["view", "update", "delete"], new() { ["precio"] = ["view"], ["notas"] = [] })
                })
            })
        };
        var schema = UserAccess.For("ana", [], false, profiles).Restrict("demo", TestSchema.Build() with { OverrideWarnings = ["ventas.categoria: x"] });
        Assert.Empty(schema.OverrideWarnings); // nombran tablas que quizá no ve

        var producto = Assert.Single(schema.Tables);
        Assert.Equal("producto", producto.Name);
        Assert.Equal(DataOperation.View | DataOperation.Update | DataOperation.Delete, producto.Access);
        Assert.False(producto.CanCreate);
        Assert.True(producto.CanUpdate);
        Assert.True(producto.CanDelete);
        Assert.False(producto.CanExport);
        Assert.False(producto.CanImport);

        Assert.True(producto.FindColumn("notas")!.Hidden);
        Assert.True(producto.FindColumn("precio")!.IsReadOnly);
        Assert.False(producto.FindColumn("precio")!.Hidden);
        Assert.False(producto.FindColumn("codigo")!.IsReadOnly);
    }

    [Fact]
    public void Las_relaciones_hacia_tablas_que_no_ve_desaparecen()
    {
        var profiles = new[] { Profile("p", users: ["ana"], connections: new() { ["demo"] = Conn([], new() { ["ventas.categoria"] = Table(["view"]) }) }) };
        var full = TestSchema.Build().FindTable("ventas", "categoria")!;
        Assert.NotEmpty(full.ReferencedBy);

        var categoria = Assert.Single(UserAccess.For("ana", [], false, profiles).Restrict("demo", TestSchema.Build()).Tables);
        Assert.Empty(categoria.ReferencedBy); // producto no se ve
    }

    [Fact]
    public void Las_operaciones_se_leen_sin_importar_mayusculas()
    {
        Assert.Equal(DataOperation.View | DataOperation.Delete, DataOperations.Parse(["VIEW", " delete ", "inventada"]));
        Assert.Equal(["view", "import"], DataOperations.ToNames(DataOperation.View | DataOperation.Import));
    }
}
