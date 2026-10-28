using Mantenedores.Core.Audit;
using Mantenedores.Metadata;

namespace Mantenedores.Tests;

/// <summary>Auditoría de cambios (P-044, D-065): valores registrados y almacenes de SQLite y archivo.</summary>
public sealed class AuditTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("audit-tests-").FullName;

    [Fact]
    public void La_clave_es_estable_ordenada_y_escapada()
    {
        Assert.Equal("linea=2&pedido_id=10", AuditValues.Key([new("Pedido_Id", "10"), new("linea", "2")]));
        Assert.Equal("codigo=a%26b%3Dc", AuditValues.Key([new("codigo", "a&b=c")]));
    }

    [Fact]
    public void La_edicion_guarda_solo_lo_que_cambio_y_los_archivos_por_tamano()
    {
        var producto = TestSchema.Build().FindTable("ventas", "producto")!;
        var before = new Dictionary<string, object?> { ["id"] = 1, ["codigo"] = "A", ["precio"] = 10m, ["foto__size"] = 100L, ["categoria_id__label"] = "X" };
        var after = new Dictionary<string, object?> { ["id"] = 1, ["codigo"] = "A", ["precio"] = 12.5m, ["foto__size"] = 250L, ["categoria_id__label"] = "Y" };

        var (oldV, newV) = AuditValues.Diff(producto, before, after)!.Value;
        Assert.Equal("""{"precio":10,"foto":"(100 bytes)"}""", oldV);
        Assert.Equal("""{"precio":12.5,"foto":"(250 bytes)"}""", newV);
        Assert.Null(AuditValues.Diff(producto, before, before));
        Assert.Equal("id=1", AuditValues.Key(producto, after));
    }

    [Fact]
    public void Las_columnas_ocultas_no_se_registran()
    {
        var producto = TestSchema.Build().FindTable("ventas", "producto")!;
        producto = producto with { Columns = producto.Columns.Select(c => c.Name == "notas" ? c with { Hidden = true } : c).ToList() };
        var values = AuditValues.Columns(producto, new Dictionary<string, object?> { ["id"] = 1, ["notas"] = "secreto" });
        Assert.Equal(["id"], values.Keys.ToList());
    }

    public static TheoryData<string> Stores => new() { "sqlite", "file" };
    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Con_perfil_solo_se_ven_las_tablas_y_columnas_permitidas(string kind)
    {
        // P-095: ana ve todo "demo" salvo la tabla sueldo, y en producto no ve el costo; de "rrhh" no ve nada.
        var store = await Store(kind);
        AuditEntry E(string conn, string table, string values) => new()
        {
            At = DateTimeOffset.UtcNow, User = "x", Connection = conn, Schema = "ventas", Table = table,
            Operation = AuditOperation.Update, RowKey = "id=1", OldValues = values, NewValues = values
        };
        await store.WriteAsync([E("demo", "producto", """{"precio":1,"costo":2}"""), E("demo", "sueldo", """{"monto":9}"""),
            E("demo", "categoria", """{"nombre":"a"}"""), E("rrhh", "producto", """{"precio":3}""")], default);
        var profile = new Mantenedores.Core.Security.SecurityProfile
        {
            Id = "p", Name = "p", Users = ["ana"],
            Connections = new Dictionary<string, Mantenedores.Core.Security.ConnectionRule>
            {
                ["demo"] = new()
                {
                    Operations = ["view"],
                    Tables = new Dictionary<string, Mantenedores.Core.Security.TableRule>
                    {
                        ["ventas.sueldo"] = new() { Operations = [] },
                        ["ventas.producto"] = new()
                        {
                            Operations = ["view"],
                            Columns = new Dictionary<string, Mantenedores.Core.Security.ColumnRule> { ["costo"] = new() { Operations = [] } }
                        }
                    }
                }
            }
        };
        var ana = Mantenedores.Core.Security.UserAccess.For("ana", [], false, [profile]);

        var query = AuditAccess.Restrict(new AuditQuery(), ana, await store.TablesAsync(default));
        var page = await store.QueryAsync(query, default);
        Assert.Equal(2, page.Total);
        Assert.Equal(["categoria", "producto"], page.Entries.Select(e => e.Table).Order().ToList());
        var producto = AuditAccess.Mask(page.Entries.Single(e => e.Table == "producto"), ana);
        Assert.False(AuditAccess.IsEmptyForUser(producto));
        // P-103: si solo cambió el costo, que ana no ve, la edición se omite.
        Assert.True(AuditAccess.IsEmptyForUser(AuditAccess.Mask(page.Entries.Single(e => e.Table == "producto") with
            { OldValues = "{\"costo\":2}", NewValues = "{\"costo\":3}" }, ana)));
        Assert.Equal("""{"precio":1}""", producto.OldValues);

        // Sin nada permitido, nada; sin restricciones, todo.
        var nadie = Mantenedores.Core.Security.UserAccess.For("luis", [], false, [profile]);
        Assert.Equal(0, (await store.QueryAsync(AuditAccess.Restrict(new AuditQuery(), nadie, await store.TablesAsync(default)), default)).Total);
        Assert.Equal(4, (await store.QueryAsync(AuditAccess.Restrict(new AuditQuery(), Mantenedores.Core.Security.UserAccess.All, []), default)).Total);
    }


    private async Task<IAuditStore> Store(string kind)
    {
        if (kind == "file") return new FileAuditStore(Path.Combine(_root, "audit.jsonl"));
        var db = new MetadataDatabase(new MetadataOptions(), _root);
        await new MetadataMigrator(db).MigrateAsync();
        return new SqlAuditStore(db);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Guarda_filtra_pagina_y_purga(string kind)
    {
        var store = await Store(kind);
        var now = DateTimeOffset.UtcNow;
        AuditEntry E(int i, AuditOperation op, string table, DateTimeOffset at, string? user = "ana") => new()
        {
            At = at, User = user, Connection = "demo", Schema = "ventas", Table = table, Operation = op,
            RowKey = $"id={i}", NewValues = $$"""{"n":{{i}}}"""
        };
        await store.WriteAsync([
            E(1, AuditOperation.Create, "producto", now.AddDays(-400)),
            E(1, AuditOperation.Update, "producto", now.AddMinutes(-5)),
            E(2, AuditOperation.Create, "producto", now.AddMinutes(-4), "luis"),
            E(1, AuditOperation.Delete, "categoria", now.AddMinutes(-3))
        ], default);

        var all = await store.QueryAsync(new AuditQuery(), default);
        Assert.Equal(4, all.Total);
        Assert.Equal("categoria", all.Entries[0].Table); // lo más reciente primero

        var history = await store.QueryAsync(new AuditQuery { Connection = "demo", Schema = "ventas", Table = "producto", RowKey = "id=1" }, default);
        Assert.Equal([AuditOperation.Update, AuditOperation.Create], history.Entries.Select(e => e.Operation));
        Assert.Equal("""{"n":1}""", history.Entries[0].NewValues);

        Assert.Equal(1, (await store.QueryAsync(new AuditQuery { User = "luis" }, default)).Total);
        Assert.Equal(1, (await store.QueryAsync(new AuditQuery { Operation = AuditOperation.Delete }, default)).Total);
        Assert.Equal(3, (await store.QueryAsync(new AuditQuery { From = now.AddHours(-1) }, default)).Total);
        Assert.Equal(1, (await store.QueryAsync(new AuditQuery { To = now.AddDays(-1) }, default)).Total);

        var page2 = await store.QueryAsync(new AuditQuery { Page = 2, PageSize = 3 }, default);
        Assert.Single(page2.Entries);
        Assert.Equal(4, page2.Total);

        Assert.Equal(1, await store.PurgeAsync(now.AddDays(-365), default));
        Assert.Equal(3, (await store.QueryAsync(new AuditQuery(), default)).Total);
    }

    [Fact]
    public async Task El_servicio_respeta_la_configuracion_y_no_falla_si_el_almacen_falla()
    {
        var store = await Store("file");
        var entry = new AuditEntry { Connection = "demo", Schema = "s", Table = "t", Operation = AuditOperation.Create };

        await new AuditService(store, new AuditOptions { Enabled = false }).RecordAsync([entry], default);
        Assert.Equal(0, (await store.QueryAsync(new AuditQuery(), default)).Total);

        await new AuditService(store, new AuditOptions()).RecordAsync([entry], default);
        var saved = Assert.Single((await store.QueryAsync(new AuditQuery(), default)).Entries);
        Assert.True(saved.At > DateTimeOffset.UtcNow.AddMinutes(-1));

        Exception? logged = null;
        await new AuditService(new FailingStore(), new AuditOptions(), ex => logged = ex).RecordAsync([entry], default);
        Assert.IsType<InvalidOperationException>(logged);

        Assert.Equal(0, await new AuditService(store, new AuditOptions { RetentionDays = 0 }).PurgeExpiredAsync(default));
    }

    private sealed class FailingStore : IAuditStore
    {
        public Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct) => throw new InvalidOperationException("caída");
        public Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AuditTable>> TablesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct) => throw new NotImplementedException();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
