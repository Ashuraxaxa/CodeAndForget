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
    public async Task Guarda_tokens_propuestas_y_el_permiso_de_muestra()
    {
        // P-113: migración 007 y los almacenes de la IA, en cada motor de metadata.
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();

        var conns = new SqlConnectionStore(database, Protection(database));
        var id = "s" + Guid.NewGuid().ToString("N")[..8];
        await conns.SaveAsync(new ConnectionDefinition { Id = id, Name = "Con muestra", Engine = DatabaseEngine.Postgres, ConnectionString = "Host=x", AllowSamples = true }, default);
        Assert.True((await conns.GetAsync(id, default))!.AllowSamples);
        await conns.SaveAsync(new ConnectionDefinition { Id = id, Name = "Sin muestra", Engine = DatabaseEngine.Postgres, ConnectionString = "Host=x" }, default);
        Assert.False((await conns.GetAsync(id, default))!.AllowSamples);

        var tokens = new SqlAccessTokenStore(database);
        var secret = Mantenedores.Core.Security.AccessToken.NewSecret();
        var token = new Mantenedores.Core.Security.AccessToken
        {
            Id = Guid.NewGuid().ToString("N"), Name = "IA", Owner = "Ana" + id, Roles = ["admin"], Prefix = secret[..10],
            Hash = Mantenedores.Core.Security.AccessToken.HashOf(secret), CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        await tokens.SaveAsync(token, default);
        var found = await tokens.FindByHashAsync(token.Hash, default);
        Assert.Equal(("IA", ("ana" + id).ToLowerInvariant(), "admin"), (found!.Name, found.Owner, Assert.Single(found.Roles)));
        Assert.True(Math.Abs((found.ExpiresAt!.Value - token.ExpiresAt!.Value).TotalSeconds) < 1);
        await tokens.TouchAsync(token.Id, DateTimeOffset.UtcNow, default);
        Assert.NotNull((await tokens.FindByHashAsync(token.Hash, default))!.LastUsedAt);
        Assert.Single(await tokens.ListAsync("ana" + id, default));
        Assert.False(await tokens.DeleteAsync(token.Id, "otro", default)); // solo el dueño
        Assert.True(await tokens.DeleteAsync(token.Id, "ANA" + id, default));

        var proposals = new SqlProposalStore(database);
        var p = new Mantenedores.Core.Overrides.OverrideProposal
        {
            Id = Guid.NewGuid().ToString("N"), Connection = id, Schema = "ventas", Table = "producto",
            Value = new Mantenedores.Core.Overrides.TableOverride { Label = "Productos" }, Reason = "Nombre más claro", CreatedBy = "ia", CreatedAt = DateTimeOffset.UtcNow
        };
        await proposals.SaveAsync(p, default);
        Assert.Equal("Productos", Assert.Single(await proposals.ListAsync(id, "pending", default)).Value.Label);
        await proposals.SaveAsync(p with { Status = "discarded", ResolvedBy = "ana", ResolvedAt = DateTimeOffset.UtcNow }, default);
        Assert.Empty(await proposals.ListAsync(id, "pending", default));
        Assert.Equal("ana", (await proposals.GetAsync(p.Id, default))!.ResolvedBy);
        await proposals.DeleteAllAsync(id, default);
        Assert.Null(await proposals.GetAsync(p.Id, default));
        await conns.DeleteAsync(id, default);
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
    public async Task Los_avisos_entre_instancias_suben_y_se_leen_una_vez()
    {
        var database = Database();
        await new MetadataMigrator(database).MigrateAsync();
        var store = new SqlChangeStampStore(database);
        var a = new Core.Services.ChangeStamps(store);
        var b = new Core.Services.ChangeStamps(store);
        var scope = Core.Services.ChangeStamps.Overrides("c" + Guid.NewGuid().ToString("N")[..8]);

        await b.PollAsync(default); // primera revisión: solo toma nota
        // Varias instancias suben el mismo ámbito a la vez: ninguna pierde su aviso.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.BumpAsync(scope, default)));
        Assert.Equal(8, (await store.ListAsync(default))[scope]);

        Assert.Contains(scope, await b.PollAsync(default));
        Assert.DoesNotContain(scope, await b.PollAsync(default));
        await a.PublishAsync(scope, default);
        Assert.Contains(scope, await b.PollAsync(default));
        await a.PollAsync(default);
        Assert.DoesNotContain(scope, await a.PollAsync(default)); // lo propio no vuelve como aviso
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
        // Con perfil (P-095): solo lo permitido, por conexión completa o por tabla suelta.
        Assert.Contains(await store.TablesAsync(default), t => t.Connection == conn && t.Table == "producto");
        Assert.Equal(2, (await store.QueryAsync(new Core.Audit.AuditQuery
        {
            Connection = conn, AllowedConnections = [], AllowedTables = [new(conn, "ventas", "producto")]
        }, default)).Total);
        Assert.Equal(0, (await store.QueryAsync(new Core.Audit.AuditQuery
        {
            Connection = conn, AllowedConnections = ["otra"], AllowedTables = [new(conn, "ventas", "otra_tabla")]
        }, default)).Total);
        Assert.Equal(-1, (await store.QueryAsync(new Core.Audit.AuditQuery { Connection = conn, CountTotal = false }, default)).Total);
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
