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

    /// <summary>Cadena de conexión a otra base del mismo servidor (para migrar desde cero).</summary>
    protected abstract string WithDatabase(string connectionString, string database);

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
    public async Task La_auditoria_se_guarda_filtra_pagina_y_purga()
    {
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();
        var store = new Mantenedores.Metadata.SqlAuditStore(database);
        var conn = "a" + Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        Core.Audit.AuditEntry E(Core.Audit.AuditOperation op, string key, DateTimeOffset at) => new()
        {
            At = at, User = "ana", Connection = conn, Schema = "ventas", Table = "producto", Operation = op,
            RowKey = key, OldValues = "{\"precio\":10}", NewValues = "{\"precio\":\"ñandú\"}"
        };
        await store.WriteAsync([
            E(Core.Audit.AuditOperation.Create, "id=1", now.AddDays(-400)),
            E(Core.Audit.AuditOperation.Update, "id=1", now.AddMinutes(-2)),
            E(Core.Audit.AuditOperation.Create, "id=2", now.AddMinutes(-1))
        ], default);

        var history = await store.QueryAsync(new Core.Audit.AuditQuery { Connection = conn, RowKey = "id=1" }, default);
        Assert.Equal(2, history.Total);
        Assert.Equal(Core.Audit.AuditOperation.Update, history.Entries[0].Operation);
        Assert.Equal("{\"precio\":\"ñandú\"}", history.Entries[0].NewValues);
        Assert.True(Math.Abs((history.Entries[0].At - now.AddMinutes(-2)).TotalSeconds) < 1);

        Assert.Equal(2, (await store.QueryAsync(new Core.Audit.AuditQuery { Connection = conn, From = now.AddHours(-1) }, default)).Total);
        var page = await store.QueryAsync(new Core.Audit.AuditQuery { Connection = conn, Page = 2, PageSize = 2 }, default);
        Assert.Single(page.Entries);

        Assert.True(await store.PurgeAsync(now.AddDays(-365), default) >= 1);
        Assert.Equal(2, (await store.QueryAsync(new Core.Audit.AuditQuery { Connection = conn }, default)).Total);
    }

    [Fact]
    public async Task Varias_instancias_migrando_a_la_vez_aplican_cada_script_una_vez()
    {
        // Base nueva: las cuatro "instancias" compiten por crear todo desde cero (P-081).
        var name = "mnt_" + Guid.NewGuid().ToString("N")[..8];
        await db.ScalarAsync($"CREATE DATABASE {name}");
        var options = new MetadataOptions { Provider = Provider, ConnectionString = WithDatabase(db.ConnectionString, name) };
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => new MetadataMigrator(new MetadataDatabase(options, Path.GetTempPath())).MigrateAsync())));
        var all = runs.SelectMany(r => r).ToList();
        Assert.Equal(MetadataMigrator.Scripts(Provider.ToString()).Count, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
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

    [Fact]
    public async Task Guarda_y_lee_la_foto_del_esquema()
    {
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();
        var store = new SqlSnapshotStore(database);
        var id = "c" + Guid.NewGuid().ToString("N")[..8];
        var snapshot = new Mantenedores.Core.Schema.SchemaSnapshot(
        [
            new("ventas", "Producto", Mantenedores.Core.Schema.TableKind.Table,
                [new("Id", "int", false), new("Nombre", "nvarchar(120)", false)], ["Id"], [])
        ]);

        await store.SaveAsync(id, snapshot, null, default);
        await store.SaveAsync(id, snapshot, "ana", default); // reemplaza
        var got = await store.GetAsync(id, default);
        Assert.Equal("ana", got!.TakenBy);
        Assert.True(DateTimeOffset.UtcNow - got.TakenAt < TimeSpan.FromMinutes(5));
        Assert.Empty(Mantenedores.Core.Schema.SchemaDiff.Compare(snapshot, got.Snapshot));

        await store.DeleteAsync(id, default);
        Assert.Null(await store.GetAsync(id, default));
    }
}

public sealed class PostgresMetadataStoreTests(PostgresFixture db) : MetadataStoreContractTests<PostgresFixture>(db)
{
    protected override MetadataProvider Provider => MetadataProvider.Postgres;
    protected override string WithDatabase(string cs, string database) =>
        new Npgsql.NpgsqlConnectionStringBuilder(cs) { Database = database }.ConnectionString;
}

public sealed class SqlServerMetadataStoreTests(SqlServerFixture db) : MetadataStoreContractTests<SqlServerFixture>(db)
{
    protected override MetadataProvider Provider => MetadataProvider.SqlServer;
    protected override string WithDatabase(string cs, string database) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs) { InitialCatalog = database }.ConnectionString;
}
