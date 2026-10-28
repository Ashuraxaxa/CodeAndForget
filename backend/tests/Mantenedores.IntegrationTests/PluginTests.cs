using System.Runtime.Loader;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Services;
using Mantenedores.IntegrationTests.Infrastructure;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.IntegrationTests;

/// <summary>Un motor cargado como plugin desde una carpeta (P-064, D-092), con el CRUD de siempre encima.</summary>
public sealed class PluginTests(PostgresFixture db) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("plugins-").FullName;

    /// <summary>Copia la salida de Mantenedores.TestPlugin a &lt;carpeta&gt;/Mantenedores.TestPlugin/, como en un servidor.</summary>
    private string Install()
    {
        // bin/<Configuración>/net8.0 de estas pruebas → la misma salida del plugin.
        var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var (tfm, config) = (here.Name, here.Parent!.Name);
        var tests = here.Parent!.Parent!.Parent!.Parent!.FullName;
        var output = Path.Combine(tests, "Mantenedores.TestPlugin", "bin", config, tfm);
        Assert.True(File.Exists(Path.Combine(output, "Mantenedores.TestPlugin.dll")), $"No está compilado el plugin en {output}");

        var target = Path.Combine(_dir, "Mantenedores.TestPlugin");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(output)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return _dir;
    }

    [Fact]
    public async Task Carga_el_motor_en_su_propio_contexto_y_el_crud_funciona_igual()
    {
        var plugin = Assert.Single(ProviderPlugins.Load(Install()));
        var provider = Assert.Single(plugin.Providers);
        Assert.Equal("PgPlugin", provider.Engine.Name);
        Assert.Equal("PostgreSQL (plugin)", provider.DisplayName);

        // Sus dependencias son las de su carpeta, no las de la app; Core es el mismo.
        var context = AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly)!;
        Assert.NotSame(AssemblyLoadContext.Default, context);
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(provider.Dialect.GetType().Assembly));
        Assert.NotSame(typeof(PostgresProvider).Assembly, provider.Dialect.GetType().Assembly);
        Assert.Same(typeof(IDatabaseProvider).Assembly, provider.GetType().GetInterfaces()[0].Assembly);

        var registry = new ProviderRegistry([new PostgresProvider(), provider]);
        Assert.Equal(["Postgres", "PgPlugin"], registry.All.Select(p => p.Engine.Name));
        var schemas = new SchemaService(new InMemoryConnectionStore(new ConnectionDefinition
        {
            Id = "plug", Name = "Plugin", Engine = new DatabaseEngine("pgplugin"), // sin distinguir mayúsculas
            ConnectionString = db.ConnectionString, Schemas = ["ventas"]
        }), registry);
        var crud = new CrudService(schemas, new CrudOptions());

        var page = await crud.ListAsync("plug", "ventas", "producto", new ListRequest { PageSize = 2 }, default);
        Assert.Equal(2, page.Rows.Count);
        Assert.True(page.Total > 2);
    }

    [Fact]
    public void Una_carpeta_sin_su_dll_o_un_motor_repetido_detienen_el_arranque()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Roto"));
        var ex = Assert.Throws<InvalidOperationException>(() => ProviderPlugins.Load(_dir));
        Assert.Contains("Roto.dll", ex.Message);
        Assert.Throws<InvalidOperationException>(() => ProviderPlugins.Load(Path.Combine(_dir, "no-existe")));

        var dup = Assert.Throws<InvalidOperationException>(() => new ProviderRegistry([new PostgresProvider(), new PostgresProvider()]));
        Assert.Contains("Postgres", dup.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
