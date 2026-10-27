using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.IntegrationTests.Infrastructure;
using Mantenedores.Metadata;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Mantenedores.IntegrationTests;

/// <summary>
/// El almacén de metadata se prueba en los motores donde puede vivir (D-023): PostgreSQL y SQL Server
/// (SQLite se prueba en las unitarias). Usa la misma base del fixture; las tablas llevan prefijo mnt_.
/// </summary>
[Trait("Category", "Integration")]
public abstract class MetadataStoreContractTests<TFixture>(TFixture db) : IClassFixture<TFixture>
    where TFixture : DatabaseFixture
{
    protected abstract MetadataProvider Provider { get; }

    private MetadataDatabase Database() =>
        new(new MetadataOptions { Provider = Provider, ConnectionString = db.ConnectionString }, Path.GetTempPath());

    /// <summary>Data Protection con las llaves en la base, como una instancia de la app.</summary>
    private IDataProtectionProvider Protection(MetadataDatabase database)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(MetadataSetup.ApplicationName);
        services.AddOptions<KeyManagementOptions>().Configure(o => o.XmlRepository = new DatabaseXmlRepository(database));
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public async Task Migra_guarda_y_comparte_llaves_entre_instancias()
    {
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();
        Assert.Empty(await new MetadataMigrator(database).MigrateAsync()); // idempotente

        // Dos "instancias" independientes que solo comparten la base de metadata.
        var a = new SqlConnectionStore(database, Protection(database));
        var b = new SqlConnectionStore(database, Protection(database));

        var id = "c" + Guid.NewGuid().ToString("N")[..8];
        await a.SaveAsync(new ConnectionDefinition
        {
            Id = id.ToUpperInvariant(), Name = "Ventas", Engine = DatabaseEngine.MySql,
            ConnectionString = "Server=srv;Database=ventas;User ID=u;Password=ñandú", Schemas = ["ventas"]
        }, default);

        var read = await b.GetAsync(id, default);
        Assert.Equal("Server=srv;Database=ventas;User ID=u;Password=ñandú", read!.ConnectionString);
        Assert.Equal(DatabaseEngine.MySql, read.Engine);
        Assert.Equal(["ventas"], read.Schemas);

        await b.SaveAsync(read with { Name = "Ventas (editada)" }, default);
        Assert.Equal("Ventas (editada)", (await a.GetAsync(id, default))!.Name);
        Assert.Contains(await a.ListAsync(default), c => c.Id == id);

        Assert.True(await a.DeleteAsync(id, default));
        Assert.Null(await b.GetAsync(id, default));
    }

    [Fact]
    public async Task Guarda_y_lee_ajustes()
    {
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();
        var store = new SqlOverrideStore(database);
        var id = "c" + Guid.NewGuid().ToString("N")[..8];

        var o = new Mantenedores.Core.Overrides.TableOverride
        {
            Label = "Productos — ñandú",
            Columns = new Dictionary<string, Mantenedores.Core.Overrides.ColumnOverride> { ["Precio"] = new() { Min = 0.5m, Pattern = "\\d+" } }
        };
        await store.SaveAsync(id.ToUpperInvariant(), "ventas", "Producto", o, "ana", default);
        await store.SaveAsync(id, "ventas", "Producto", o with { Group = "Catálogo" }, "ana", default); // reemplaza

        var got = Assert.Single(await store.ListAsync(id, default));
        Assert.Equal(("ventas", "Producto"), got.Key);
        Assert.Equal("Productos — ñandú", got.Value.Label);
        Assert.Equal("Catálogo", got.Value.Group);
        Assert.Equal(0.5m, got.Value.Columns!["Precio"].Min);
        Assert.Equal("\\d+", got.Value.Columns["Precio"].Pattern);

        await store.DeleteAllAsync(id, default);
        Assert.Empty(await store.ListAsync(id, default));
    }
}

public sealed class PostgresMetadataStoreTests(PostgresFixture db) : MetadataStoreContractTests<PostgresFixture>(db)
{
    protected override MetadataProvider Provider => MetadataProvider.Postgres;
}

public sealed class SqlServerMetadataStoreTests(SqlServerFixture db) : MetadataStoreContractTests<SqlServerFixture>(db)
{
    protected override MetadataProvider Provider => MetadataProvider.SqlServer;
}
