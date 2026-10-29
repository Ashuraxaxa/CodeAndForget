using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;
using Mantenedores.IntegrationTests.Infrastructure;

namespace Mantenedores.IntegrationTests;

/// <summary>
/// Batería común (D-033): todo motor soportado debe pasar exactamente estas pruebas contra su base
/// de ejemplo. Para agregar un motor se hereda esta clase con su fixture; no se copian pruebas.
/// Los nombres se escriben en snake_case y el fixture los traduce al estilo de cada base.
/// </summary>
[Trait("Category", "Integration")]
public abstract class ProviderContractTests<TFixture>(TFixture db) : IClassFixture<TFixture>
    where TFixture : DatabaseFixture
{
    protected TFixture Db { get; } = db;
    private const string Conn = DatabaseFixture.ConnectionId;

    protected string N(string logical) => Db.Name(logical);
    protected string Schema => Db.SampleSchema;

    protected static Dictionary<string, JsonElement> Body(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));

    protected static Dictionary<string, string> Key(params (string Key, object Value)[] values) =>
        values.ToDictionary(v => v.Key, v => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture)!);

    /// <summary>Código único por prueba para no depender del orden de ejecución.</summary>
    protected static string UniqueCode() => "T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task<TableInfo> TableAsync(string logical) =>
        (await Db.Schemas.GetTableAsync(Conn, Schema, N(logical), default)).Table;

    // ---------- Caché compartida (P-050) ----------

    [Fact]
    public async Task El_esquema_leido_pasa_por_la_cache_compartida_sin_perder_nada()
    {
        var raw = await Db.Schemas.GetRawSchemaAsync(Conn, default);
        var json = Mantenedores.Core.Services.SchemaJson.Serialize(raw);
        var back = Mantenedores.Core.Services.SchemaJson.Deserialize(json)!;
        Assert.Equal(json, Mantenedores.Core.Services.SchemaJson.Serialize(back));
        Assert.Equal(raw.Hash, back.Hash);
        Assert.Empty(SchemaDiff.Compare(SchemaSnapshot.From(raw), SchemaSnapshot.From(back)));
    }

    // ---------- Versión ----------

    [Fact]
    public void El_servidor_cumple_la_version_minima() =>
        Assert.True(Db.MajorVersion >= Db.MinimumMajorVersion,
            $"Versión {Db.MajorVersion} menor que la mínima {Db.MinimumMajorVersion} ({Db.Image}).");

    [Fact]
    public async Task Informa_el_servidor_y_lo_acepta_si_cumple_el_minimo()
    {
        var def = new Core.Connections.ConnectionDefinition
        {
            Id = "prueba", Name = "Prueba", Engine = Db.Engine, ConnectionString = Db.ConnectionString, Schemas = [Schema]
        };
        var result = await Db.Schemas.TestAsync(def, default);
        Assert.True(result.Server.IsSupported);
        Assert.Equal(Db.MajorVersion, result.Server.Version.Major);
        Assert.NotEmpty(result.Server.Settings);
        Assert.Equal(12, result.Tables);
    }

    // ---------- Lectura del esquema ----------

    [Fact]
    public async Task Lee_tablas_vistas_y_tablas_sin_pk()
    {
        var schema = (await Db.Schemas.GetContextAsync(Conn, default)).Schema;
        Assert.Equal(12, schema.Tables.Count);

        var view = schema.FindTable(Schema, N("v_productos"))!;
        Assert.Equal(TableKind.View, view.Kind);
        Assert.False(view.IsEditable);

        var log = schema.FindTable(Schema, N("log_evento"))!;
        Assert.False(log.HasPrimaryKey);
        Assert.False(log.IsEditable);
    }

    [Fact]
    public async Task Lee_pk_simple_compuesta_unicos_y_fk()
    {
        var producto = await TableAsync("producto");
        Assert.Equal([N("id")], producto.PrimaryKey, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(producto.UniqueKeys, u => u.Columns.SequenceEqual([N("codigo")], StringComparer.OrdinalIgnoreCase));

        var fk = Assert.Single(producto.ForeignKeys);
        Assert.Equal(N("categoria"), fk.RefTable, ignoreCase: true);
        Assert.Equal(N("categoria_id"), fk.Columns[0], ignoreCase: true);

        var detalle = await TableAsync("pedido_detalle");
        Assert.Equal([N("pedido_id"), N("linea")], detalle.PrimaryKey, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, detalle.ForeignKeys.Count);

        var categoria = await TableAsync("categoria");
        Assert.Contains(categoria.ReferencedBy, r => string.Equals(r.Table, N("producto"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Detecta_autorreferencia_y_tabla_puente()
    {
        var empleado = await TableAsync("empleado");
        var self = Assert.Single(empleado.ForeignKeys);
        Assert.Equal(empleado.Name, self.RefTable, ignoreCase: true);

        var puente = await TableAsync("producto_etiqueta");
        Assert.Equal(2, puente.PrimaryKey.Count);
        Assert.Equal(2, puente.ForeignKeys.Count);
    }

    [Fact]
    public async Task Las_columnas_generadas_por_la_bd_son_de_solo_lectura()
    {
        var producto = await TableAsync("producto");
        var id = producto.FindColumn(N("id"))!;
        Assert.True(id.IsIdentity);
        Assert.True(id.IsReadOnly);
        Assert.False(id.IsRequiredOnInsert);
        Assert.True(producto.FindColumn(N("creado_en"))!.HasDefault);
    }

    [Fact]
    public async Task Tipos_logicos_de_las_columnas()
    {
        var producto = await TableAsync("producto");
        Assert.Equal(LogicalType.String, producto.FindColumn(N("codigo"))!.Type);
        Assert.Equal(20, producto.FindColumn(N("codigo"))!.MaxLength);
        Assert.Equal(LogicalType.Integer, producto.FindColumn(N("categoria_id"))!.Type);
        Assert.Equal(LogicalType.Decimal, producto.FindColumn(N("precio"))!.Type);
        Assert.Equal(LogicalType.DateTime, producto.FindColumn(N("creado_en"))!.Type);
        Assert.True(producto.FindColumn(N("creado_en"))!.HasTimeZone);

        var cliente = await TableAsync("cliente");
        Assert.Equal(LogicalType.Uuid, cliente.FindColumn(N("id"))!.Type);
        Assert.Equal(LogicalType.Date, cliente.FindColumn(N("fecha_alta"))!.Type);

        Assert.Equal(LogicalType.Time, (await TableAsync("empleado")).FindColumn(N("hora_entrada"))!.Type);
        Assert.Equal(LogicalType.Boolean, (await TableAsync("categoria")).FindColumn(N("activo"))!.Type);
    }

    [Fact]
    public async Task Elige_la_columna_a_mostrar()
    {
        Assert.Equal(N("nombre"), (await TableAsync("categoria")).DisplayColumn, ignoreCase: true);
        Assert.Equal(N("razon_social"), (await TableAsync("cliente")).DisplayColumn, ignoreCase: true);
    }

    // ---------- Listado ----------

    [Fact]
    public async Task Lista_con_filtro_orden_y_etiqueta_de_fk()
    {
        var result = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest
        {
            Filters = new Dictionary<string, string> { [N("categoria_id")] = "1" },
            Sort = [new SortSpec(N("precio"), Descending: true)]
        }, default);

        Assert.True(result.Total >= 2);
        Assert.All(result.Rows, r => Assert.Equal("Bebidas", r[N("categoria_id") + QueryBuilder.LabelSuffix]));
        var precios = result.Rows.Select(r => Convert.ToDecimal(r[N("precio")])).ToList();
        Assert.Equal(precios.OrderByDescending(p => p), precios);
    }

    [Fact]
    public async Task La_pagina_siguiente_por_clave_es_la_misma_que_por_desplazamiento()
    {
        // P-098: con el orden de la PK, "after" da la misma página 2 sin OFFSET; con otro orden se ignora.
        var p1 = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Page = 1, PageSize = 2 }, default);
        var p2 = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Page = 2, PageSize = 2 }, default);
        var last = Convert.ToString(p1.Rows[^1][N("id")], System.Globalization.CultureInfo.InvariantCulture)!;
        var byKey = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Page = 2, PageSize = 2, After = last }, default);
        Assert.Equal(p2.Rows.Select(r => r[N("id")]), byKey.Rows.Select(r => r[N("id")]));
        Assert.Equal(p2.Total, byKey.Total);

        var sorted = new ListRequest { Page = 2, PageSize = 2, Sort = [new SortSpec(N("nombre"))] };
        Assert.Equal((await Db.Crud.ListAsync(Conn, Schema, N("producto"), sorted, default)).Rows.Select(r => r[N("id")]),
            (await Db.Crud.ListAsync(Conn, Schema, N("producto"), sorted with { After = last }, default)).Rows.Select(r => r[N("id")]));
    }

    [Fact]
    public async Task Pagina_sin_repetir_filas()
    {
        var p1 = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Page = 1, PageSize = 2 }, default);
        var p2 = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Page = 2, PageSize = 2 }, default);
        Assert.Equal(2, p1.Rows.Count);
        Assert.Empty(p1.Rows.Select(r => r[N("id")]).Intersect(p2.Rows.Select(r => r[N("id")])));
    }

    [Theory]
    [InlineData("agua", true)]
    [InlineData("AGUA", true)]   // sin distinguir mayúsculas
    [InlineData("a%a", false)]   // como comodín coincidiría con "Agua mineral"; escapado no existe
    [InlineData("a_u", false)]   // como comodín coincidiría con "Agua"; escapado no existe
    [InlineData("'; --", false)] // intento de inyección: es solo texto
    public async Task Busca_en_textos_escapando_comodines(string search, bool found)
    {
        var result = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Search = search }, default);
        Assert.Equal(found, result.Total > 0);
    }

    [Fact]
    public async Task Lista_vistas_y_tablas_sin_pk()
    {
        var view = await Db.Crud.ListAsync(Conn, Schema, N("v_productos"), new ListRequest(), default);
        Assert.True(view.Total >= 4);
        var log = await Db.Crud.ListAsync(Conn, Schema, N("log_evento"), new ListRequest(), default);
        Assert.True(log.Total >= 4); // filas que dejó el trigger al cargar la base
    }

    [Fact]
    public async Task Autorreferencia_trae_la_etiqueta_del_jefe()
    {
        var result = await Db.Crud.ListAsync(Conn, Schema, N("empleado"), new ListRequest
        {
            Filters = new Dictionary<string, string> { [N("jefe_id")] = "1" }
        }, default);
        Assert.Contains(result.Rows, r => Equals(r[N("jefe_id") + QueryBuilder.LabelSuffix], "Ana Gerente"));
    }

    [Fact]
    public async Task Lookup_para_combos_de_fk()
    {
        // "bebi" y no "beb": otras pruebas crean categorías con códigos hexadecimales (UniqueCode), que pueden traer "BEB".
        var items = await Db.Crud.LookupAsync(Conn, Schema, N("categoria"), "bebi", 20, default);
        var item = Assert.Single(items);
        Assert.Equal("Bebidas", item.Label);
        Assert.Equal(1L, Convert.ToInt64(item.Value));
    }

    // ---------- Escritura ----------

    // ---------- FK de varias columnas (P-030) ----------

    [Fact]
    public async Task Fk_compuesta_trae_etiqueta_y_combo_con_todas_las_columnas_de_la_clave()
    {
        var label = N("sucursal_codigo") + QueryBuilder.LabelSuffix;
        var bodegas = await Db.Crud.ListAsync(Conn, Schema, N("bodega"), new ListRequest { Sort = [new SortSpec(N("id"))] }, default);
        Assert.Equal("Santiago Centro", bodegas.Rows[0][label]);
        var fk = Assert.Single((await TableAsync("bodega")).ForeignKeys);
        Assert.Equal(N("sucursal_codigo"), fk.LabelColumn, ignoreCase: true);

        // El combo de la tabla referenciada devuelve la clave completa.
        var options = await Db.Crud.LookupAsync(Conn, Schema, N("sucursal"), "val", 20, default);
        var valpo = Assert.Single(options);
        Assert.Equal("Valparaíso", valpo.Label);
        var key = Assert.IsAssignableFrom<IDictionary<string, object?>>(valpo.Value);
        Assert.Equal("CL", Convert.ToString(key.First(k => string.Equals(k.Key, N("pais"), StringComparison.OrdinalIgnoreCase)).Value)?.Trim());
        Assert.Equal("VAL", key.First(k => string.Equals(k.Key, N("codigo"), StringComparison.OrdinalIgnoreCase)).Value);

        // Alta con lo elegido en el combo: la etiqueta vuelve resuelta.
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("bodega"), Body(
            (N("nombre"), "Bodega " + UniqueCode()), (N("pais"), "PE"), (N("sucursal_codigo"), "LIM")), default);
        try { Assert.Equal("Lima", created[label]); }
        finally { await Db.Crud.DeleteAsync(Conn, Schema, N("bodega"), Key((N("id"), created[N("id")]!)), default); }
    }

    // ---------- Muestra para la IA (P-113) ----------

    [Fact]
    public async Task La_muestra_trae_lo_mas_reciente_solo_si_la_conexion_lo_permite()
    {
        Core.Services.SchemaService Schemas(bool allow) => new(new InMemoryConnectionStore(new Core.Connections.ConnectionDefinition
        {
            Id = "muestra", Name = "Muestra", Engine = Db.Engine, ConnectionString = Db.ConnectionString, Schemas = [Schema], AllowSamples = allow
        }), new Core.Providers.ProviderRegistry([Db.Provider]));

        var off = new Core.Services.CrudService(Schemas(false), new Core.Services.CrudOptions());
        var denied = await Assert.ThrowsAsync<OperationNotAllowedException>(() => off.SampleAsync("muestra", Schema, N("categoria"), 10, default));
        Assert.Contains("muestra", denied.Message);

        var on = new Core.Services.CrudService(Schemas(true), new Core.Services.CrudOptions());
        var sample = await on.SampleAsync("muestra", Schema, N("categoria"), 2, default);
        Assert.Equal(2, sample.Rows.Count);
        Assert.Equal([N("id")], sample.OrderedBy, StringComparer.OrdinalIgnoreCase); // sin fecha: por la PK, de mayor a menor
        var max = await Db.ScalarAsync($"SELECT MAX({Db.Provider.Dialect.Quote(N("id"))}) FROM {Db.Provider.Dialect.Table(Schema, N("categoria"))}");
        Assert.Equal(max, Convert.ToInt64(sample.Rows[0][N("id")]));
        Assert.True(Convert.ToInt64(sample.Rows[0][N("id")]) > Convert.ToInt64(sample.Rows[1][N("id")]));
        Assert.Equal(1000, Core.Services.CrudService.MaxSampleRows);
    }

    // ---------- Copiar ajustes entre conexiones (P-111) ----------

    [Fact]
    public async Task Copia_los_ajustes_de_una_conexion_a_otra_con_vista_previa()
    {
        // Dos conexiones a la misma base: como la misma base en dos servidores.
        var store = new InMemoryConnectionStore(
            new Core.Connections.ConnectionDefinition { Id = "origen", Name = "Origen", Engine = Db.Engine, ConnectionString = Db.ConnectionString, Schemas = [Schema] },
            new Core.Connections.ConnectionDefinition { Id = "destino", Name = "Destino", Engine = Db.Engine, ConnectionString = Db.ConnectionString, Schemas = [Schema] });
        var overrides = new InMemoryOverrideStore();
        var schemas = new Core.Services.SchemaService(store, new Core.Providers.ProviderRegistry([Db.Provider]), overrides);
        var copier = new Core.Services.OverrideCopier(schemas);

        var producto = (await schemas.GetRawSchemaAsync("origen", default)).FindTable(Schema, N("producto"))!;
        var child = (await schemas.GetRawSchemaAsync("origen", default)).Tables.SelectMany(t => t.ForeignKeys)
            .First(f => Same(f.RefTable, producto.Name) && Same(f.Table, N("pedido_detalle")));
        await overrides.SaveAsync("origen", producto.Schema, producto.Name,
            new Core.Overrides.TableOverride { Label = "Productos copiados", Children = [child.Name] }, "prueba", default);
        await overrides.SaveAsync("origen", Schema, "no_existe_en_destino", new Core.Overrides.TableOverride { Label = "x" }, "prueba", default);
        await overrides.SaveAsync("origen", Schema, N("categoria"), new Core.Overrides.TableOverride
        {
            Columns = new Dictionary<string, Core.Overrides.ColumnOverride> { ["columna_inexistente"] = new() { Label = "x" } }
        }, "prueba", default);
        var cliente = (await schemas.GetRawSchemaAsync("destino", default)).FindTable(Schema, N("cliente"))!;
        await overrides.SaveAsync("destino", cliente.Schema, cliente.Name, new Core.Overrides.TableOverride { Label = "Solo en destino" }, "prueba", default);

        // Vista previa: no guarda nada.
        var preview = await copier.CopyAsync("origen", "destino", null, replaceAll: true, apply: false, "prueba", default);
        Assert.False(preview.Applied);
        Assert.Equal(Core.Services.OverrideCopyStatus.New, preview.Items.Single(i => Same(i.Table, producto.Name)).Status);
        Assert.Equal(Core.Services.OverrideCopyStatus.MissingTable, preview.Items.Single(i => i.Table == "no_existe_en_destino").Status);
        var invalid = preview.Items.Single(i => Same(i.Table, N("categoria")));
        Assert.Equal(Core.Services.OverrideCopyStatus.Invalid, invalid.Status);
        Assert.Contains("columns.columna_inexistente", invalid.Errors!.Keys);
        Assert.Equal(Core.Services.OverrideCopyStatus.Removed, preview.Items.Single(i => Same(i.Table, cliente.Name)).Status);
        Assert.Single(await overrides.ListAsync("destino", default));

        // Sin "reemplazar todo", lo propio del destino se mantiene.
        var merge = await copier.CopyAsync("origen", "destino", null, replaceAll: false, apply: true, "prueba", default);
        Assert.Equal(1, merge.Copied);
        var destino = (await schemas.GetContextAsync("destino", default)).Schema;
        Assert.Equal("Productos copiados", destino.FindTable(producto.Schema, producto.Name)!.Label);
        Assert.Equal([child.Name], destino.FindTable(producto.Schema, producto.Name)!.Children!.Select(f => f.Name));
        Assert.Equal("Solo en destino", destino.FindTable(cliente.Schema, cliente.Name)!.Label);

        // Con "reemplazar todo", se borra lo que el origen no tiene; un esquema inexistente deja todo sin tabla.
        await copier.CopyAsync("origen", "destino", null, replaceAll: true, apply: true, "prueba", default);
        Assert.Null((await schemas.GetContextAsync("destino", default)).Schema.FindTable(cliente.Schema, cliente.Name)!.Label);
        var mapped = await copier.CopyAsync("origen", "destino", new Dictionary<string, string> { [Schema] = "otro_esquema" }, false, false, null, default);
        Assert.All(mapped.Items, i => Assert.Equal(Core.Services.OverrideCopyStatus.MissingTable, i.Status));
        await Assert.ThrowsAsync<ValidationException>(() => copier.CopyAsync("destino", "DESTINO", null, false, false, null, default));

        // Solo algunas partes: las relaciones del origen, sin tocar el nombre que el destino ya tenía.
        await overrides.SaveAsync("destino", producto.Schema, producto.Name, new Core.Overrides.TableOverride { Label = "Nombre del destino" }, "prueba", default);
        await overrides.SaveAsync("origen", producto.Schema, producto.Name, new Core.Overrides.TableOverride
        {
            Label = "Nombre del origen", VirtualPrimaryKey = null, Children = [child.Name], Group = "Del origen"
        }, "prueba", default);
        var partial = await copier.CopyAsync("origen", "destino", null, replaceAll: true, apply: true, "prueba", default,
            parts: ["relations"], tables: [$"{producto.Schema}.{producto.Name}"]);
        Assert.Equal(Core.Services.OverrideCopyStatus.Replace, Assert.Single(partial.Items).Status); // "reemplazar todo" no aplica a una copia parcial
        var merged = (await overrides.ListAsync("destino", default)).Single(kv => Same(kv.Key.Table, producto.Name)).Value;
        Assert.Equal("Nombre del destino", merged.Label);
        Assert.Null(merged.Group);
        Assert.Equal([child.Name], merged.Children);
        await Assert.ThrowsAsync<ValidationException>(() => copier.CopyAsync("origen", "destino", null, false, false, null, default, parts: ["nada"]));

        // Solo entre conexiones del mismo motor.
        var otherEngine = Db.Engine == Core.Providers.DatabaseEngine.Postgres ? Core.Providers.DatabaseEngine.SqlServer : Core.Providers.DatabaseEngine.Postgres;
        await store.SaveAsync(new Core.Connections.ConnectionDefinition { Id = "otro_motor", Name = "Otro", Engine = otherEngine, ConnectionString = "Host=nadie" }, default);
        var engine = await Assert.ThrowsAsync<ValidationException>(() => copier.CopyAsync("otro_motor", "destino", null, false, false, null, default));
        Assert.Contains("mismo motor", engine.Errors["from"]);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---------- Combo de FK filtrado por otra columna (P-110) ----------

    [Fact]
    public async Task El_combo_de_area_solo_ofrece_las_de_la_empresa_y_se_valida_al_guardar()
    {
        // Como CentroCosto (EmpresaID, AreaID) y Area (EmpresaID): tablas propias de la prueba, con DDL común a los motores.
        string T(string name) => $"{Schema}.{name}";
        await Db.ExecuteAsync($"CREATE TABLE {T("lf_area")} (id int NOT NULL PRIMARY KEY, empresa int NOT NULL, nombre varchar(40) NOT NULL)");
        await Db.ExecuteAsync($"CREATE TABLE {T("lf_centro")} (id int NOT NULL PRIMARY KEY, empresa int NULL, area_id int NULL, nombre varchar(40) NOT NULL)");
        try
        {
            await Db.ExecuteAsync($"INSERT INTO {T("lf_area")} (id, empresa, nombre) VALUES (1, 1, 'Producción'), (2, 1, 'Ventas'), (3, 2, 'Bodega')");
            await Db.Schemas.RefreshAsync(Conn, default);
            var value = new Core.Overrides.TableOverride
            {
                VirtualForeignKeys = [new(["area_id"], Schema, "lf_area", ["id"])],
                LookupFilters = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["area_id"] = new Dictionary<string, string> { ["empresa"] = "empresa" } }
            };
            Core.Overrides.OverrideApplier.Validate(await Db.Schemas.GetRawSchemaAsync(Conn, default), Schema, "lf_centro", value);
            await Db.Overrides.SaveAsync(Conn, Schema, "lf_centro", value, "prueba", default);
            Db.Schemas.InvalidateOverrides(Conn);

            var fk = Assert.Single((await Db.Schemas.GetTableAsync(Conn, Schema, "lf_centro", default)).Table.ForeignKeys);
            Assert.Equal([new LookupFilter("empresa", "empresa")], fk.LookupFilters);

            // El combo: solo las áreas de la empresa pedida.
            var empresa1 = await Db.Crud.LookupAsync(Conn, Schema, "lf_area", null, 20, default, new Dictionary<string, string> { ["empresa"] = "1" });
            Assert.Equal(["Producción", "Ventas"], empresa1.Select(i => i.Label).Order());
            var empresa2 = await Db.Crud.LookupAsync(Conn, Schema, "lf_area", "bod", 20, default, new Dictionary<string, string> { ["empresa"] = "2" });
            Assert.Equal("Bodega", Assert.Single(empresa2).Label);

            // Al guardar: un área de otra empresa, o un área sin empresa, se rechaza aunque llegue por la API.
            var otra = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.CreateAsync(Conn, Schema, "lf_centro",
                Body(("id", 1), ("empresa", 1), ("area_id", 3), ("nombre", "Centro 1")), default));
            Assert.Contains("no corresponde", otra.Errors["area_id"]);
            var sinEmpresa = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.CreateAsync(Conn, Schema, "lf_centro",
                Body(("id", 1), ("empresa", null), ("area_id", 1), ("nombre", "Centro 1")), default));
            Assert.Contains("elija primero empresa", sinEmpresa.Errors["area_id"]);

            await Db.Crud.CreateAsync(Conn, Schema, "lf_centro", Body(("id", 1), ("empresa", 1), ("area_id", 2), ("nombre", "Centro 1")), default);
            await Db.Crud.CreateAsync(Conn, Schema, "lf_centro", Body(("id", 2), ("empresa", 2), ("area_id", null), ("nombre", "Sin área")), default);

            // Cambiar solo la empresa: el área que ya tenía (de la empresa 1) se revisa con el registro actual.
            var cambio = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.UpdateAsync(Conn, Schema, "lf_centro",
                Key(("id", 1)), Body(("empresa", 2)), default));
            Assert.Contains("area_id", cambio.Errors.Keys);
            await Db.Crud.UpdateAsync(Conn, Schema, "lf_centro", Key(("id", 1)), Body(("empresa", 2), ("area_id", 3)), default);
            Assert.Equal(1L, await Db.ScalarAsync($"SELECT COUNT(*) FROM {T("lf_centro")} WHERE empresa = 2 AND area_id = 3"));
        }
        finally
        {
            await Db.Overrides.DeleteAllAsync(Conn, default);
            await Db.ExecuteAsync($"DROP TABLE {T("lf_centro")}");
            await Db.ExecuteAsync($"DROP TABLE {T("lf_area")}");
            await Db.Schemas.RefreshAsync(Conn, default);
        }
    }

    // ---------- Filtro de una FK por su etiqueta (P-032) ----------

    [Fact]
    public async Task Da_el_texto_a_mostrar_de_varios_ids_para_la_auditoria()
    {
        // P-094: ids como vienen del JSON de la auditoría (número o texto), con uno que ya no existe.
        var ids = new[] { JsonSerializer.SerializeToElement(1), JsonSerializer.SerializeToElement("2"), JsonSerializer.SerializeToElement(999999) };
        var labels = await Db.Crud.LabelsAsync(Conn, Schema, N("categoria"), N("id"), ids, default);
        Assert.Equal(2, labels.Count);
        Assert.False(string.IsNullOrEmpty(labels["1"]));
        Assert.False(string.IsNullOrEmpty(labels["2"]));
        Assert.Empty(await Db.Crud.LabelsAsync(Conn, Schema, N("categoria"), N("id"), [], default));
    }

    [Fact]
    public async Task El_filtro_de_una_fk_acepta_el_id_o_el_texto_que_se_muestra()
    {
        async Task<List<object?>> Categorias(string filter) =>
            (await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest
            {
                PageSize = 200, Filters = new Dictionary<string, string> { [N("categoria_id")] = filter }
            }, default)).Rows.Select(r => r[N("categoria_id") + QueryBuilder.LabelSuffix]).Distinct().ToList();

        Assert.Equal(["Snacks"], await Categorias("snack")); // por la etiqueta, sin distinguir mayúsculas
        Assert.Equal(["Snacks"], await Categorias("2"));     // por el id, como antes
        Assert.Empty(await Categorias("no existe"));
        await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.ListAsync(Conn, Schema, N("producto"),
            new ListRequest { Filters = new Dictionary<string, string> { [N("precio")] = "caro" } }, default)); // sin FK: valor inválido
    }

    // ---------- Permisos del usuario de la base (P-073) ----------

    [Fact]
    public async Task Si_el_usuario_de_la_base_no_puede_escribir_lo_dice_claro()
    {
        const string user = "mnt_solo_lectura", password = "Lectura.2026!x";
        foreach (var sql in Db.ReadOnlyUserSql(user, password)) await Db.ExecuteAsync(sql);

        var def = new Core.Connections.ConnectionDefinition
        {
            Id = "lectura", Name = "Solo lectura", Engine = Db.Engine, ConnectionString = Db.WithCredentials(user, password), Schemas = [Schema]
        };
        var schemas = new Core.Services.SchemaService(new InMemoryConnectionStore(def), new Core.Providers.ProviderRegistry([Db.Provider]));
        var crud = new Core.Services.CrudService(schemas, new Core.Services.CrudOptions());

        Assert.NotEmpty((await crud.ListAsync("lectura", Schema, N("categoria"), new ListRequest(), default)).Rows); // leer, sí
        var ex = await Assert.ThrowsAsync<DbPermissionDeniedException>(() =>
            crud.CreateAsync("lectura", Schema, N("categoria"), Body((N("nombre"), "No entra")), default));
        Assert.Contains("no tiene permiso para crear registros en", ex.Message);
    }

    // ---------- Avisos por correo (D-073) ----------

    [Fact]
    public async Task Avisa_por_correo_solo_lo_configurado_en_la_tabla()
    {
        var before = Db.Notices.For(N("categoria")).Count;
        await WithOverrideAsync("categoria", new Core.Overrides.TableOverride
        {
            Label = "Categorías",
            Notify = new Core.Overrides.NotifyOverride { Emails = ["jefa@empresa.cl"], On = ["update", "delete"] }
        }, async () =>
        {
            var created = await Db.Crud.CreateAsync(Conn, Schema, N("categoria"), Body((N("nombre"), "Avisada")), default);
            var key = Key((N("id"), created[N("id")]!));
            await Db.Crud.UpdateAsync(Conn, Schema, N("categoria"), key, Body((N("nombre"), "Avisada 2")), default);
            await Db.Crud.DeleteAsync(Conn, Schema, N("categoria"), key, default);

            var notices = Db.Notices.For(N("categoria")).Skip(before).ToList();
            Assert.Equal([Core.Audit.AuditOperation.Update, Core.Audit.AuditOperation.Delete],
                notices.Select(n => n.Entries.Single().Operation)); // el alta no estaba pedida
            Assert.All(notices, n => Assert.Equal(["jefa@empresa.cl"], n.Notify.Emails));
            Assert.Equal("Categorías", notices[0].TableLabel);
            Assert.Contains("Avisada 2", notices[0].Entries[0].NewValues);
        });

        // Sin el ajuste, la tabla no avisa.
        await Db.Crud.CreateAsync(Conn, Schema, N("etiqueta"), Body((N("nombre"), "Sin aviso " + UniqueCode())), default);
        Assert.Empty(Db.Notices.For(N("etiqueta")));
    }

    // ---------- Tablas grandes (D-072) ----------

    [Fact]
    public async Task En_tablas_grandes_estima_el_total_sin_filtros_y_lo_corta_con_filtros()
    {
        var table = $"{Db.Provider.Dialect.Quote(Schema)}.{Db.Provider.Dialect.Quote(N("producto"))}";
        await Db.ExecuteAsync(Db.AnalyzeSql(table));
        var exact = await Db.ScalarAsync($"SELECT COUNT(*) FROM {table}");
        // Con el umbral en 1, toda tabla con estadísticas usa la estimación.
        var crud = new Core.Services.CrudService(Db.Schemas, new Core.Services.CrudOptions { EstimateCountAbove = 1 });

        var all = await crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { PageSize = 2 }, default);
        Assert.True(all.TotalIsEstimate);
        Assert.InRange(all.Total, 1, exact * 3); // una estimación: del orden del real
        Assert.Equal(2, all.Rows.Count);

        // Con filtros se cuenta, pero hasta el tope: "más de N". La página tiene que venir llena: si no, el total
        // ya se conoce sin contar.
        var search = new ListRequest { Search = "a", PageSize = 1 };
        var exactMatches = (await Db.Crud.ListAsync(Conn, Schema, N("producto"), search, default)).Total;
        Assert.True(exactMatches >= 2);
        var capped = await new Core.Services.CrudService(Db.Schemas, new Core.Services.CrudOptions { EstimateCountAbove = 1, FilteredCountCap = 1 })
            .ListAsync(Conn, Schema, N("producto"), search, default);
        Assert.False(capped.TotalIsEstimate);
        Assert.True(capped.TotalIsCapped);
        Assert.Equal(1, capped.Total);
        var underCap = await crud.ListAsync(Conn, Schema, N("producto"), search, default);
        Assert.False(underCap.TotalIsCapped);
        Assert.Equal(exactMatches, underCap.Total);

        // Página incompleta: el total es exacto sin contar, aunque la tabla sea "grande".
        var partial = await crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Search = "a", PageSize = 200 }, default);
        Assert.False(partial.TotalIsCapped);
        Assert.Equal(exactMatches, partial.Total);

        var view = await crud.ListAsync(Conn, Schema, N("v_productos"), new ListRequest(), default);
        Assert.False(view.TotalIsEstimate); // las vistas no tienen estadísticas propias

        // Con el umbral por defecto, una tabla chica se cuenta exacto.
        var normal = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest(), default);
        Assert.False(normal.TotalIsEstimate);
        Assert.False(normal.TotalIsCapped);
        Assert.Equal(exact, normal.Total);
    }

    // ---------- Concurrencia optimista (D-069) ----------

    // ---------- Acciones con procedimientos (P-061) ----------

    [Fact]
    public async Task Una_accion_llama_a_la_rutina_con_la_columna_y_lo_que_pregunta()
    {
        await Db.ScalarAsync(Db.CreateActionRoutineSql);
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), UniqueCode()), (N("nombre"), "Acción"), (N("categoria_id"), 1), (N("precio"), 10)), default);
        var key = Key((N("id"), created[N("id")]!));
        try
        {
            await Db.Schemas.RefreshAsync(Conn, default);
            var routine = Assert.Single(await Db.Schemas.GetRoutinesAsync(Conn, default), r => r.Name == "subir_precio");
            Assert.Equal(2, routine.Parameters.Count);
            var (idParam, montoParam) = (routine.Parameters[0].Name, routine.Parameters[1].Name);

            var action = new Core.Overrides.TableAction
            {
                Name = "subir", Label = "Subir precio", Routine = routine.FullName,
                Parameters = new Dictionary<string, Core.Overrides.ActionParameter>
                {
                    [idParam] = new() { Column = N("id") },
                    [montoParam] = new() { Ask = true }
                }
            };
            Assert.Empty(Core.Overrides.OverrideApplier.ValidateActions([action], await Db.Schemas.GetRoutinesAsync(Conn, default)));
            var info = await TableAsync("producto");
            await Db.Overrides.SaveAsync(Conn, info.Schema, info.Name, new Core.Overrides.TableOverride { Actions = [action] }, "prueba", default);
            Db.Schemas.InvalidateOverrides(Conn);

            var result = await Db.Crud.ExecuteActionAsync(Conn, Schema, N("producto"), "subir", key,
                new Dictionary<string, JsonElement> { [montoParam] = JsonSerializer.SerializeToElement(5.5) }, default);
            Assert.Equal(15.5m, Convert.ToDecimal(Assert.Single(result.Rows).Values.First()));
            Assert.Equal(15.5m, Convert.ToDecimal((await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default))[N("precio")]));
            Assert.Contains(Db.Audit.For(Schema, N("producto")), e => e.Operation == Core.Audit.AuditOperation.Action && e.NewValues!.Contains("subir_precio"));

            // Un valor que no calza con el tipo del parámetro se rechaza antes de llamar a la base.
            var ex = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.ExecuteActionAsync(Conn, Schema, N("producto"), "subir", key,
                new Dictionary<string, JsonElement> { [montoParam] = JsonSerializer.SerializeToElement("mucho") }, default));
            Assert.Contains(montoParam, ex.Errors.Keys);
            await Assert.ThrowsAsync<NotFoundException>(() => Db.Crud.ExecuteActionAsync(Conn, Schema, N("producto"), "otra", key, null, default));
        }
        finally
        {
            await Db.Overrides.DeleteAllAsync(Conn, default);
            await Db.ScalarAsync(Db.DropActionRoutineSql);
            await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default);
            await Db.Schemas.RefreshAsync(Conn, default);
        }
    }

    [Fact]
    public async Task Dos_ediciones_a_la_vez_con_la_misma_version_solo_una_gana()
    {
        // P-096: la fila se bloquea entre comparar la versión y escribir; la segunda espera y ve la versión nueva.
        const string V = Core.Concurrency.RowVersion.Field;
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), UniqueCode()), (N("nombre"), "Carrera"), (N("categoria_id"), 1), (N("precio"), 10)), default);
        var key = Key((N("id"), created[N("id")]!));
        var version = (string)created[V]!;
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
                {
                    try { await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 100 + round * 10 + i)), default, version); return true; }
                    catch (ConcurrencyException) { return false; }
                })));
                Assert.Equal(1, results.Count(ok => ok));
                version = (string)(await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default))[V]!;
            }
        }
        finally { await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default); }
    }

    [Fact]
    public async Task No_pisa_un_registro_que_otro_cambio_despues_de_leerlo()
    {
        const string V = Core.Concurrency.RowVersion.Field;
        var code = UniqueCode();
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), code), (N("nombre"), "Concurrencia"), (N("categoria_id"), 1), (N("precio"), 50)), default);
        var key = Key((N("id"), created[N("id")]!));

        // La versión del listado y la del detalle son la misma (el borrado desde la grilla usa la del listado).
        var listed = (await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Search = code }, default)).Rows.Single();
        var read = await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default);
        Assert.Equal(read[V], listed[V]);
        Assert.Equal(created[V], read[V]);
        var version = (string)read[V]!;

        // Otra persona guarda primero; quien tenía la versión anterior no la pisa y recibe cómo quedó.
        var other = await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 60)), default, version);
        Assert.NotEqual(version, other[V]);
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() =>
            Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 70)), default, version));
        Assert.Equal(60m, Convert.ToDecimal(ex.Current[N("precio")]));
        await Assert.ThrowsAsync<ConcurrencyException>(() => Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default, version));

        // Con la versión al día, o sin versión (importaciones, scripts), se guarda.
        var mine = await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 70)), default, (string)other[V]!);
        Assert.Equal(70m, Convert.ToDecimal(mine[N("precio")]));
        await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 71)), default);
        await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default,
            (string)(await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default))[V]!);
    }

    // ---------- Auditoría (D-065) ----------

    [Fact]
    public async Task Registra_altas_ediciones_y_bajas_con_quien_y_los_valores()
    {
        Db.Access.Current = UserAccess.For("ana", [], isAdmin: false, []);
        try
        {
            var code = UniqueCode();
            var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
                (N("codigo"), code), (N("nombre"), "Auditado"), (N("categoria_id"), 2), (N("precio"), 100)), default);
            var id = Convert.ToString(created[N("id")], System.Globalization.CultureInfo.InvariantCulture)!;
            var rowKey = Core.Audit.AuditValues.Key([new(N("id"), id)]);
            var key = Key((N("id"), created[N("id")]!));

            await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 120)), default);
            await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 120)), default); // sin cambios: no se registra
            await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default);

            var history = Db.Audit.For(Schema, N("producto"), rowKey);
            Assert.Equal([Core.Audit.AuditOperation.Create, Core.Audit.AuditOperation.Update, Core.Audit.AuditOperation.Delete],
                history.Select(e => e.Operation));
            Assert.All(history, e => Assert.Equal("ana", e.User));
            Assert.All(history, e => Assert.Equal(Conn, e.Connection));

            using var alta = JsonDocument.Parse(history[0].NewValues!);
            Assert.Equal(code, alta.RootElement.GetProperty(N("codigo")).GetString());
            Assert.Null(history[0].OldValues);

            using var antes = JsonDocument.Parse(history[1].OldValues!);
            using var despues = JsonDocument.Parse(history[1].NewValues!);
            Assert.Equal(100m, antes.RootElement.GetProperty(N("precio")).GetDecimal());
            Assert.Equal(120m, despues.RootElement.GetProperty(N("precio")).GetDecimal());
            // Solo lo que cambió (en SQL Server también la columna calculada del precio con IVA).
            Assert.False(despues.RootElement.TryGetProperty(N("codigo"), out _));
            Assert.False(despues.RootElement.TryGetProperty(N("nombre"), out _));

            using var baja = JsonDocument.Parse(history[2].OldValues!);
            Assert.Equal("Auditado", baja.RootElement.GetProperty(N("nombre")).GetString());
            Assert.Null(history[2].NewValues);
        }
        finally { Db.Access.Current = UserAccess.All; }
    }

    // ---------- Permisos por perfil (D-063) ----------

    [Fact]
    public async Task Un_perfil_restringe_tablas_columnas_y_operaciones()
    {
        var code = UniqueCode();
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), code), (N("nombre"), "Secreto"), (N("categoria_id"), 2), (N("precio"), 100)), default);
        var key = Key((N("id"), created[N("id")]!));

        var profile = new SecurityProfile
        {
            Id = "p", Name = "Editor de códigos", Users = ["ana"],
            Connections = new Dictionary<string, ConnectionRule>
            {
                [Conn] = new()
                {
                    Tables = new Dictionary<string, TableRule>
                    {
                        [$"{Schema}.{N("producto")}"] = new()
                        {
                            Operations = ["view", "update"],
                            Columns = new Dictionary<string, ColumnRule> { [N("precio")] = new() { Operations = ["view"] }, [N("nombre")] = new() }
                        }
                    }
                }
            }
        };
        Db.Access.Current = UserAccess.For("ana", [], isAdmin: false, [profile]);
        try
        {
            var schema = (await Db.Schemas.GetContextAsync(Conn, default)).Schema;
            Assert.Equal([N("producto")], schema.Tables.Select(t => t.Name));
            await Assert.ThrowsAsync<NotFoundException>(() => Db.Schemas.GetTableAsync(Conn, Schema, N("categoria"), default));

            // Ve la fila sin la columna oculta y sin la etiqueta de una tabla que no ve; no la encuentra por el dato oculto.
            var row = await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default);
            Assert.False(row.ContainsKey(N("nombre")));
            Assert.False(row.ContainsKey(N("categoria_id") + QueryBuilder.LabelSuffix));
            Assert.Equal(0, (await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { Search = "Secreto" }, default)).Total);

            // Edita lo permitido; lo de solo lectura se ignora.
            var updated = await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("codigo"), code + "X"), (N("precio"), 1)), default);
            Assert.Equal(code + "X", updated[N("codigo")]);
            Assert.Equal(100m, Convert.ToDecimal(updated[N("precio")]));

            await Assert.ThrowsAsync<ForbiddenException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body((N("codigo"), UniqueCode())), default));
            await Assert.ThrowsAsync<ForbiddenException>(() => Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default));
            await Assert.ThrowsAsync<ForbiddenException>(() => Db.Crud.PrepareExportAsync(Conn, Schema, N("producto"), new ListRequest(), Core.Transfer.TransferFormat.Csv, default));
        }
        finally { Db.Access.Current = UserAccess.All; }

        Assert.Equal("Secreto", (await Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default))[N("nombre")]);
        await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default);
    }

    [Fact]
    public async Task Crud_completo_con_pk_identity()
    {
        var code = UniqueCode();
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), code), (N("nombre"), "Prueba"), (N("categoria_id"), 2), (N("precio"), "990.5")), default);

        var id = created[N("id")]!;
        Assert.Equal(code, created[N("codigo")]);
        Assert.Equal("Snacks", created[N("categoria_id") + QueryBuilder.LabelSuffix]);
        Assert.Equal(990.5m, Convert.ToDecimal(created[N("precio")]));

        var key = Key((N("id"), id));
        var updated = await Db.Crud.UpdateAsync(Conn, Schema, N("producto"), key, Body((N("precio"), 1000)), default);
        Assert.Equal(1000m, Convert.ToDecimal(updated[N("precio")]));

        await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), key, default);
        await Assert.ThrowsAsync<NotFoundException>(() => Db.Crud.GetAsync(Conn, Schema, N("producto"), key, default));
    }

    [Fact]
    public async Task Insercion_en_tabla_con_trigger_devuelve_la_clave()
    {
        var table = $"{Db.Provider.Dialect.Quote(Schema)}.{Db.Provider.Dialect.Quote(N("log_evento"))}";
        var before = await Db.ScalarAsync($"SELECT COUNT(*) FROM {table}");

        var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), UniqueCode()), (N("nombre"), "Con trigger"), (N("categoria_id"), 1), (N("precio"), 1)), default);

        Assert.NotNull(created[N("id")]);
        Assert.Equal(before + 1, await Db.ScalarAsync($"SELECT COUNT(*) FROM {table}"));
    }

    [Fact]
    public async Task Crud_con_pk_compuesta()
    {
        var linea = Random.Shared.Next(100, 30_000);
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("pedido_detalle"), Body(
            (N("pedido_id"), 1), (N("linea"), linea), (N("producto_id"), 2), (N("cantidad"), 3), (N("precio"), 1890)), default);
        Assert.Equal("Jugo de naranja 1L", created[N("producto_id") + QueryBuilder.LabelSuffix]);

        var key = Key((N("pedido_id"), 1), (N("linea"), linea));
        var updated = await Db.Crud.UpdateAsync(Conn, Schema, N("pedido_detalle"), key, Body((N("cantidad"), 7)), default);
        Assert.Equal(7L, Convert.ToInt64(updated[N("cantidad")]));

        await Db.Crud.DeleteAsync(Conn, Schema, N("pedido_detalle"), key, default);
        await Assert.ThrowsAsync<NotFoundException>(() => Db.Crud.GetAsync(Conn, Schema, N("pedido_detalle"), key, default));
    }

    [Fact]
    public async Task Pk_uuid_y_fecha_generadas_por_la_bd()
    {
        var created = await Db.Crud.CreateAsync(Conn, Schema, N("cliente"), Body(
            (N("rut"), UniqueCode()), (N("razon_social"), "Cliente de prueba"), (N("email"), null)), default);

        Assert.IsType<Guid>(created[N("id")]);
        Assert.NotEqual(Guid.Empty, created[N("id")]);
        Assert.IsType<DateOnly>(created[N("fecha_alta")]); // mismo tipo en todos los motores
        Assert.Null(created[N("email")]);
    }

    [Fact]
    public async Task Hora_y_booleano_ida_y_vuelta()
    {
        var empleado = await Db.Crud.CreateAsync(Conn, Schema, N("empleado"), Body(
            (N("nombre"), "Eva"), (N("jefe_id"), 1), (N("hora_entrada"), "08:30")), default);
        Assert.Equal(new TimeOnly(8, 30), empleado[N("hora_entrada")]); // mismo tipo en todos los motores

        var categoria = await Db.Crud.CreateAsync(Conn, Schema, N("categoria"), Body(
            (N("nombre"), UniqueCode()), (N("activo"), false)), default);
        Assert.Equal(false, categoria[N("activo")]);
    }

    [Fact]
    public async Task Numeros_que_no_caben_en_decimal_viajan_como_texto()
    {
        var pedido = await TableAsync("pedido");
        Assert.True(pedido.FindColumn(N("monto_total"))!.IsLargeNumber);

        const string huge = "1234567890123456789012345678901234.5678";
        var row = await Db.Crud.GetAsync(Conn, Schema, N("pedido"), Key((N("id"), 1)), default);
        Assert.Equal(huge, row[N("monto_total")]);

        var updated = await Db.Crud.UpdateAsync(Conn, Schema, N("pedido"), Key((N("id"), 1)),
            Body((N("monto_total"), "-9876543210987654321098765432109876.1234")), default);
        Assert.Equal("-9876543210987654321098765432109876.1234", updated[N("monto_total")]);

        var filtered = await Db.Crud.ListAsync(Conn, Schema, N("pedido"), new ListRequest
        {
            Filters = new Dictionary<string, string> { [N("monto_total")] = "-9876543210987654321098765432109876.1234" }
        }, default);
        Assert.Equal(1, filtered.Total);

        await Db.Crud.UpdateAsync(Conn, Schema, N("pedido"), Key((N("id"), 1)), Body((N("monto_total"), huge)), default);
    }

    [Fact]
    public async Task Etiquetas_de_fk_vuelven_con_su_nombre_final()
    {
        var row = await Db.Crud.GetAsync(Conn, Schema, N("pedido_detalle"), Key((N("pedido_id"), 1), (N("linea"), 2)), default);
        Assert.Contains(N("producto_id") + QueryBuilder.LabelSuffix, row.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(row.Keys, k => k.StartsWith("__lbl", StringComparison.Ordinal));
    }

    // ---------- Errores ----------

    [Fact]
    public async Task Valida_antes_de_llegar_a_la_bd()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"),
            Body((N("codigo"), "X"), (N("precio"), "abc"), ("no_existe", 1)), default));
        Assert.Contains(N("precio"), ex.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("no_existe", ex.Errors.Keys);
    }

    // Los errores del motor llegan traducidos y con el mismo formato en todos los motores (P-003).

    [Fact]
    public async Task Violacion_de_unico_indica_la_columna()
    {
        var ex = await Assert.ThrowsAsync<ConstraintViolationException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), "BEB-001"), (N("nombre"), "Duplicado"), (N("categoria_id"), 1), (N("precio"), 1)), default));
        Assert.Equal(DbErrorKind.Unique, ex.Kind);
        Assert.Contains(N("codigo"), ex.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Violacion_de_fk_al_insertar_indica_la_columna()
    {
        var ex = await Assert.ThrowsAsync<ConstraintViolationException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), UniqueCode()), (N("nombre"), "Sin categoría"), (N("categoria_id"), 99_999), (N("precio"), 1)), default));
        Assert.Equal(DbErrorKind.ForeignKey, ex.Kind);
        Assert.Contains(N("categoria_id"), ex.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Violacion_de_fk_al_borrar_indica_quien_lo_usa()
    {
        var ex = await Assert.ThrowsAsync<ConstraintViolationException>(() =>
            Db.Crud.DeleteAsync(Conn, Schema, N("categoria"), Key((N("id"), 1)), default));
        Assert.Equal(DbErrorKind.ForeignKey, ex.Kind);
        Assert.StartsWith("No se puede eliminar: lo usan registros de ", ex.Message);
    }

    [Fact]
    public async Task Violacion_de_check_se_informa_como_regla()
    {
        var ex = await Assert.ThrowsAsync<ConstraintViolationException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), UniqueCode()), (N("nombre"), "Precio negativo"), (N("categoria_id"), 1), (N("precio"), -5)), default));
        Assert.Equal(DbErrorKind.Check, ex.Kind);
    }

    [Fact]
    public async Task Vistas_y_tablas_sin_pk_no_aceptan_escritura()
    {
        await Assert.ThrowsAsync<OperationNotAllowedException>(() =>
            Db.Crud.CreateAsync(Conn, Schema, N("v_productos"), Body((N("nombre"), "x")), default));
        await Assert.ThrowsAsync<OperationNotAllowedException>(() =>
            Db.Crud.CreateAsync(Conn, Schema, N("log_evento"), Body((N("detalle"), "x")), default));
    }
    // ---------- Ajustes por tabla y columna (P-011) ----------

    /// <summary>Aplica un ajuste mientras corre <paramref name="body"/> y lo quita al terminar (el fixture es compartido).</summary>
    private async Task WithOverrideAsync(string logicalTable, Core.Overrides.TableOverride value, Func<Task> body)
    {
        var raw = await Db.Schemas.GetRawSchemaAsync(Conn, default);
        var table = raw.FindTable(Schema, N(logicalTable))!;
        Core.Overrides.OverrideApplier.Validate(raw, table.Schema, table.Name, value);
        await Db.Overrides.SaveAsync(Conn, table.Schema, table.Name, value, "prueba", default);
        Db.Schemas.InvalidateOverrides(Conn);
        try { await body(); }
        finally
        {
            await Db.Overrides.DeleteAllAsync(Conn, default);
            Db.Schemas.InvalidateOverrides(Conn);
        }
    }

    private static bool HasKey(IDictionary<string, object?> row, string name) =>
        row.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Un_ajuste_cambia_listado_y_escritura_sin_reiniciar_y_sobrevive_al_refrescar()
    {
        var o = new Core.Overrides.TableOverride
        {
            Label = "Productos",
            DefaultSort = "-" + N("precio"),
            Columns = new Dictionary<string, Core.Overrides.ColumnOverride>
            {
                [N("atributos")] = new() { Hidden = true },
                [N("codigo")] = new() { Pattern = "[A-Z]-[A-Z0-9]+", Label = "Código" },
                [N("precio")] = new() { Max = 100000 }
            }
        };
        await WithOverrideAsync("producto", o, async () =>
        {
            Assert.Equal("Productos", (await TableAsync("producto")).Label);

            var list = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest { PageSize = 200 }, default);
            Assert.All(list.Rows, r => Assert.False(HasKey(r, N("atributos")))); // oculta: no se lee
            var precios = list.Rows.Select(r => Convert.ToDecimal(r[N("precio")])).ToList();
            Assert.Equal(precios.OrderByDescending(p => p), precios); // orden por defecto

            var bad = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
                (N("codigo"), "minusculas"), (N("nombre"), "x"), (N("categoria_id"), 1), (N("precio"), 200000)), default));
            Assert.Equal(2, bad.Errors.Count); // patrón y máximo, juntos

            // Escribir una columna oculta se ignora: el valor no llega a la base.
            var created = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
                (N("codigo"), UniqueCode()), (N("nombre"), "Con ajuste"), (N("categoria_id"), 1), (N("precio"), 10),
                (N("atributos"), "{\"a\":1}")), default);
            Assert.False(HasKey(created, N("atributos")));
            var table = $"{Db.Provider.Dialect.Quote(Schema)}.{Db.Provider.Dialect.Quote(N("producto"))}";
            Assert.Equal(0, await Db.ScalarAsync(
                $"SELECT COUNT(*) FROM {table} WHERE {Db.Provider.Dialect.Quote(N("id"))} = {Convert.ToInt64(created[N("id")])} " +
                $"AND {Db.Provider.Dialect.Quote(N("atributos"))} IS NOT NULL"));

            // Refrescar el esquema vuelve a leer la base y el ajuste se mantiene.
            await Db.Schemas.RefreshAsync(Conn, default);
            Assert.True((await TableAsync("producto")).FindColumn(N("atributos"))!.Hidden);
            await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), Key((N("id"), created[N("id")]!)), default);
        });

        Assert.Null((await TableAsync("producto")).Label); // sin el ajuste, vuelve a lo automático
    }

    [Fact]
    public async Task Pk_y_fk_virtuales_en_una_vista()
    {
        var o = new Core.Overrides.TableOverride
        {
            VirtualPrimaryKey = [N("id")],
            VirtualForeignKeys = [new Core.Overrides.VirtualForeignKey([N("id")], Schema, N("producto"), [N("id")])]
        };
        await WithOverrideAsync("v_productos", o, async () =>
        {
            var view = await TableAsync("v_productos");
            Assert.False(view.IsEditable); // una vista sigue siendo de solo lectura
            Assert.Contains((await TableAsync("producto")).ReferencedBy, r => string.Equals(r.Table, view.Name, StringComparison.OrdinalIgnoreCase));

            // La PK virtual permite leer por clave; la FK virtual trae la etiqueta como una declarada.
            var row = await Db.Crud.GetAsync(Conn, Schema, N("v_productos"), Key((N("id"), 1)), default);
            Assert.Equal("Agua mineral 500ml", row[N("id") + QueryBuilder.LabelSuffix]);

            var lookup = await Db.Crud.LookupAsync(Conn, Schema, N("v_productos"), "Agua", 10, default);
            Assert.Contains(lookup, i => i.Label == "Agua mineral 500ml");
        });
    }

    [Fact]
    public async Task Columna_a_mostrar_por_ajuste_cambia_las_etiquetas_de_quien_la_referencia()
    {
        await WithOverrideAsync("categoria", new Core.Overrides.TableOverride { DisplayColumn = N("id") }, async () =>
        {
            var list = await Db.Crud.ListAsync(Conn, Schema, N("producto"), new ListRequest(), default);
            // La columna a mostrar es la misma clave: no hay etiqueta.
            Assert.All(list.Rows, r => Assert.False(HasKey(r, N("categoria_id") + QueryBuilder.LabelSuffix)));
        });
    }
    // ---------- Diferencias de esquema (P-013) ----------

    [Fact]
    public async Task Borrar_una_columna_en_la_base_se_avisa_y_no_rompe_el_mantenedor()
    {
        var d = Db.Provider.Dialect;
        var name = N("diff_prueba");
        var table = $"{d.Quote(Schema)}.{d.Quote(name)}";
        string Q(string c) => d.Quote(N(c));
        await Db.ScalarAsync($"CREATE TABLE {table} ({Q("id")} INT NOT NULL PRIMARY KEY, {Q("nombre")} VARCHAR(20), {Q("vieja")} INT)");
        try
        {
            await Db.Schemas.RefreshAsync(Conn, default);
            await Db.Schemas.AcceptSchemaAsync(Conn, "prueba", default); // referencia: con la tabla nueva
            var raw = await Db.Schemas.GetRawSchemaAsync(Conn, default);
            var info = raw.FindTable(Schema, name)!;
            await Db.Overrides.SaveAsync(Conn, info.Schema, info.Name, new Core.Overrides.TableOverride
            {
                Columns = new Dictionary<string, Core.Overrides.ColumnOverride> { [N("vieja")] = new() { Label = "Vieja" } }
            }, "prueba", default);

            await Db.ScalarAsync($"ALTER TABLE {table} DROP COLUMN {Q("vieja")}");
            await Db.ScalarAsync($"ALTER TABLE {table} ADD {Q("nueva")} INT");
            await Db.Schemas.RefreshAsync(Conn, default);

            var diff = await Db.Schemas.GetDiffAsync(Conn, default);
            Assert.Equal(2, diff.Changes.Count);
            Assert.Contains(diff.Changes, c => c.Kind == SchemaChangeKind.ColumnRemoved && string.Equals(c.Column, N("vieja"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(diff.Changes, c => c.Kind == SchemaChangeKind.ColumnAdded && string.Equals(c.Column, N("nueva"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(diff.OverrideWarnings, w => w.Contains(N("vieja"), StringComparison.OrdinalIgnoreCase));

            // El mantenedor sigue funcionando con la columna nueva.
            await Db.Crud.CreateAsync(Conn, Schema, name, Body((N("id"), 1), (N("nombre"), "x"), (N("nueva"), 5)), default);
            var list = await Db.Crud.ListAsync(Conn, Schema, name, new ListRequest(), default);
            Assert.Equal(5, Convert.ToInt32(Assert.Single(list.Rows)[N("nueva")]));

            // Aceptar deja la nueva referencia: sin cambios pendientes.
            Assert.Empty((await Db.Schemas.AcceptSchemaAsync(Conn, "prueba", default)).Changes);
        }
        finally
        {
            await Db.ScalarAsync($"DROP TABLE {table}");
            await Db.Overrides.DeleteAllAsync(Conn, default);
            await Db.Schemas.RefreshAsync(Conn, default);
            await Db.Snapshots.DeleteAsync(Conn, default);
        }
    }
    [Fact]
    public async Task Un_indice_unico_cuenta_como_clave_unica_y_su_error_nombra_la_columna()
    {
        // P-031: un índice único (no una restricción UNIQUE) también impide duplicados.
        var d = Db.Provider.Dialect;
        var name = N("uidx_prueba");
        var table = $"{d.Quote(Schema)}.{d.Quote(name)}";
        string Q(string c) => d.Quote(N(c));
        await Db.ScalarAsync($"CREATE TABLE {table} ({Q("id")} INT NOT NULL PRIMARY KEY, {Q("codigo")} VARCHAR(20) NOT NULL)");
        try
        {
            await Db.ScalarAsync($"CREATE UNIQUE INDEX {d.Quote(N("ux_uidx_codigo"))} ON {table} ({Q("codigo")})");
            await Db.Schemas.RefreshAsync(Conn, default);
            var info = await TableAsync("uidx_prueba");
            Assert.Contains(info.UniqueKeys, u => u.Columns.Count == 1 && string.Equals(u.Columns[0], N("codigo"), StringComparison.OrdinalIgnoreCase));

            await Db.Crud.CreateAsync(Conn, Schema, name, Body((N("id"), 1), (N("codigo"), "A")), default);
            var ex = await Assert.ThrowsAsync<ConstraintViolationException>(() =>
                Db.Crud.CreateAsync(Conn, Schema, name, Body((N("id"), 2), (N("codigo"), "A")), default));
            Assert.Contains(ex.Errors.Keys, k => string.Equals(k, N("codigo"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await Db.ScalarAsync($"DROP TABLE {table}");
            await Db.Schemas.RefreshAsync(Conn, default);
        }
    }

    [Fact]
    public async Task Detecta_solo_un_cambio_de_esquema_sin_pulsar_refrescar()
    {
        var d = Db.Provider.Dialect;
        var name = N("auto_prueba");
        var table = $"{d.Quote(Schema)}.{d.Quote(name)}";
        string Q(string c) => d.Quote(N(c));
        await Db.ScalarAsync($"CREATE TABLE {table} ({Q("id")} INT NOT NULL PRIMARY KEY, {Q("vieja")} INT)");
        try
        {
            await Db.Schemas.RefreshAsync(Conn, default);
            await Db.Crud.ListAsync(Conn, Schema, name, new ListRequest(), default);

            // Relectura periódica (P-086): sin cambios no reemplaza nada; con una columna nueva, sí.
            Assert.Empty(await Db.Schemas.ReloadIfChangedAsync(Conn, default));
            await Db.ScalarAsync($"ALTER TABLE {table} ADD {Q("nueva")} INT");
            var changes = await Db.Schemas.ReloadIfChangedAsync(Conn, default);
            Assert.Contains(changes, c => c.Kind == SchemaChangeKind.ColumnAdded && string.Equals(c.Column, N("nueva"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains((await TableAsync("auto_prueba")).Columns, c => string.Equals(c.Name, N("nueva"), StringComparison.OrdinalIgnoreCase));

            // Una columna borrada con el esquema viejo en memoria: mensaje claro, y el siguiente intento ya funciona.
            await Db.ScalarAsync($"ALTER TABLE {table} DROP COLUMN {Q("vieja")}");
            var ex = await Assert.ThrowsAsync<SchemaChangedException>(() => Db.Crud.ListAsync(Conn, Schema, name, new ListRequest(), default));
            Assert.Contains("cambió", ex.Message);
            Assert.Empty((await Db.Crud.ListAsync(Conn, Schema, name, new ListRequest(), default)).Rows);
        }
        finally
        {
            await Db.ScalarAsync($"DROP TABLE {table}");
            await Db.Schemas.RefreshAsync(Conn, default);
        }
    }

    // ---------- Maestro-detalle (P-014) ----------

    [Fact]
    public async Task Filtro_exacto_trae_las_hijas_de_un_registro_y_no_las_parecidas()
    {
        var detalle = await Db.Crud.ListAsync(Conn, Schema, N("pedido_detalle"), new ListRequest
        {
            PageSize = 200,
            Exact = new Dictionary<string, string> { [N("pedido_id")] = "1" }
        }, default);
        Assert.NotEmpty(detalle.Rows);
        Assert.All(detalle.Rows, r => Assert.Equal(1L, Convert.ToInt64(r[N("pedido_id")])));

        // En un texto, "contiene" trae BEB-001 y BEB-002; el exacto, solo uno.
        var contiene = await Db.Crud.ListAsync(Conn, Schema, N("producto"),
            new ListRequest { Filters = new Dictionary<string, string> { [N("codigo")] = "BEB-00" } }, default);
        var exacto = await Db.Crud.ListAsync(Conn, Schema, N("producto"),
            new ListRequest { Exact = new Dictionary<string, string> { [N("codigo")] = "BEB-001" } }, default);
        Assert.True(contiene.Total >= 2);
        Assert.Equal(1, exacto.Total);

        // La tabla padre conoce sus hijas.
        var pedido = await TableAsync("pedido");
        Assert.Contains(pedido.Children!, f => string.Equals(f.Table, N("pedido_detalle"), StringComparison.OrdinalIgnoreCase));
    }
    // ---------- Relaciones N:M (P-015) ----------

    [Fact]
    public async Task Relacion_n_m_se_lee_y_se_sincroniza_en_una_transaccion()
    {
        var producto = await TableAsync("producto");
        var link = Assert.Single(producto.ManyToMany);
        Assert.Equal(N("etiqueta"), link.TargetTable, ignoreCase: true);
        Assert.True((await TableAsync("producto_etiqueta")).IsBridge);

        var key = Key((N("id"), 1));
        async Task<List<long>> Linked() => (await Db.Crud.GetLinksAsync(Conn, Schema, N("producto"), key, link.Name, default))
            .Select(i => Convert.ToInt64(i.Value)).OrderBy(v => v).ToList();
        JsonElement V(long v) => JsonSerializer.SerializeToElement(v);

        var original = await Linked();
        Assert.Equal([1L], original); // la base de ejemplo: producto 1 con etiqueta 1
        Assert.NotNull((await Db.Crud.GetLinksAsync(Conn, Schema, N("producto"), key, link.Name, default))[0].Label);
        try
        {
            // Quita la 1 y agrega la 2 (con un duplicado, que se ignora).
            var result = await Db.Crud.SetLinksAsync(Conn, Schema, N("producto"), key, link.Name, [V(2), V(2)], default);
            Assert.Equal([2L], result.Select(i => Convert.ToInt64(i.Value)));
            Assert.Equal([1L, 2L], (await Db.Crud.SetLinksAsync(Conn, Schema, N("producto"), key, link.Name, [V(1), V(2)], default))
                .Select(i => Convert.ToInt64(i.Value)).OrderBy(v => v));

            // Un valor que no existe en la otra tabla falla entero: no queda nada a medias.
            await Assert.ThrowsAsync<ConstraintViolationException>(() =>
                Db.Crud.SetLinksAsync(Conn, Schema, N("producto"), key, link.Name, [V(2), V(99999)], default));
            Assert.Equal([1L, 2L], await Linked());

            Assert.Empty(await Db.Crud.SetLinksAsync(Conn, Schema, N("producto"), key, link.Name, [], default));
        }
        finally
        {
            await Db.Crud.SetLinksAsync(Conn, Schema, N("producto"), key, link.Name, original.Select(V).ToList(), default);
        }
        Assert.Equal(original, await Linked());
    }
    // ---------- Exportar e importar (P-017) ----------

    [Theory]
    [InlineData(Core.Transfer.TransferFormat.Csv)]
    [InlineData(Core.Transfer.TransferFormat.Xlsx)]
    [InlineData(Core.Transfer.TransferFormat.Json)]
    [InlineData(Core.Transfer.TransferFormat.Xml)]
    public async Task Exporta_el_listado_filtrado_en_cada_formato(Core.Transfer.TransferFormat format)
    {
        var request = new ListRequest
        {
            Exact = new Dictionary<string, string> { [N("categoria_id")] = "1" },
            Sort = [new SortSpec(N("codigo"))]
        };
        var plan = await Db.Crud.PrepareExportAsync(Conn, Schema, N("producto"), request, format, default);
        var ms = new MemoryStream();
        await plan.WriteAsync(ms, default);
        ms.Position = 0;

        var file = await Core.Transfer.TableReader.ReadAsync(ms, format, 1000, default);
        Assert.Equal(plan.Rows, file.Rows.Count);
        Assert.Contains(N("categoria_id") + QueryBuilder.LabelSuffix, file.Headers); // el texto de la FK va al lado
        Assert.All(file.Rows, r => Assert.Equal("Bebidas", r[N("categoria_id") + QueryBuilder.LabelSuffix]));
        Assert.Contains(file.Rows, r => r[N("codigo")] == "BEB-001");
    }

    private static Stream Csv(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Importar_actualizando_por_clave_cambia_las_existentes_y_crea_las_nuevas()
    {
        var prefix = UniqueCode();
        var a = await Db.Crud.CreateAsync(Conn, Schema, N("producto"), Body(
            (N("codigo"), prefix + "-A"), (N("nombre"), "Original A"), (N("categoria_id"), 1), (N("precio"), 10)), default);
        var idA = Convert.ToString(a[N("id")], System.Globalization.CultureInfo.InvariantCulture)!;
        var header = $"{N("id")},{N("codigo")},nombre,{N("categoria_id")},precio\n";
        var created = new List<object?>();
        try
        {
            // La clave la genera la base: una fila con un id que no existe no se puede crear con ese id.
            var bad = header + $"{idA},{prefix}-A,Cambiado A,1,99\n,{prefix}-B,Nuevo B,2,20\n999999999,{prefix}-C,No,1,5";
            var preview = await Db.Crud.PreviewImportAsync(Conn, Schema, N("producto"), Csv(bad), Core.Transfer.TransferFormat.Csv, null, default,
                Core.Services.ImportMode.Upsert);
            var error = Assert.Single(preview.Errors);
            Assert.Equal(4, error.Line);
            Assert.Contains("la genera la base", error.Message);
            Assert.Contains(preview.Targets, t => t.IsKey && string.Equals(t.Name, N("id"), StringComparison.OrdinalIgnoreCase));

            var ok = header + $"{idA},{prefix}-A,Cambiado A,1,99\n,{prefix}-B,Nuevo B,2,20";
            var plan = await Db.Crud.PreviewImportAsync(Conn, Schema, N("producto"), Csv(ok), Core.Transfer.TransferFormat.Csv, null, default,
                Core.Services.ImportMode.Upsert);
            Assert.Empty(plan.Errors);
            Assert.Equal((1, 1), (plan.Created, plan.Updated));

            var done = await Db.Crud.ImportAsync(Conn, Schema, N("producto"), Csv(ok), Core.Transfer.TransferFormat.Csv, null, default,
                Core.Services.ImportMode.Upsert);
            Assert.Equal((2, 1, 1), (done.Imported, done.Created, done.Updated));

            var rowA = await Db.Crud.GetAsync(Conn, Schema, N("producto"), Key((N("id"), idA)), default);
            Assert.Equal("Cambiado A", rowA[N("nombre")]);
            Assert.Equal(99m, Convert.ToDecimal(rowA[N("precio")]));
            var rowsB = (await Db.Crud.ListAsync(Conn, Schema, N("producto"),
                new ListRequest { Filters = new Dictionary<string, string> { [N("codigo")] = prefix + "-B" } }, default)).Rows;
            created.Add(Assert.Single(rowsB)[N("id")]);

            // En la auditoría: la existente como edición (solo lo que cambió) y la nueva como importación.
            var history = Db.Audit.For(Schema, N("producto"), Core.Audit.AuditValues.Key([new(N("id"), idA)]));
            var edit = history.Last();
            Assert.Equal(Core.Audit.AuditOperation.Update, edit.Operation);
            Assert.Contains("Cambiado A", edit.NewValues);

            // Sin el modo actualizar, la misma fila de A sería un alta (y choca con su código único).
            var insertOnly = await Db.Crud.ImportAsync(Conn, Schema, N("producto"), Csv(header + $"{idA},{prefix}-A,Otra,1,1"),
                Core.Transfer.TransferFormat.Csv, null, default);
            Assert.Equal(0, insertOnly.Imported);
        }
        finally
        {
            await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), Key((N("id"), idA)), default);
            foreach (var id in created) await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), Key((N("id"), id!)), default);
        }
    }

    [Fact]
    public async Task Importa_csv_en_una_transaccion_y_nada_queda_a_medias()
    {
        var prefix = UniqueCode();
        string Line(string code, string precio) => $"{code},Importado {code},2,{precio}";
        var header = $"{N("codigo")},nombre,{N("categoria_id")},precio\n"; // "nombre"/"precio" se asocian sin distinguir el estilo

        async Task<long> Count() => (await Db.Crud.ListAsync(Conn, Schema, N("producto"),
            new ListRequest { Filters = new Dictionary<string, string> { [N("codigo")] = prefix } }, default)).Total;
        try
        {
            // Vista previa con una fila inválida: informa la línea y no graba.
            var bad = header + string.Join("\n", Line(prefix + "-1", "10"), Line(prefix + "-2", "abc"), Line(prefix + "-3", "30"));
            var preview = await Db.Crud.PreviewImportAsync(Conn, Schema, N("producto"), Csv(bad), Core.Transfer.TransferFormat.Csv, null, default);
            Assert.Equal(3, preview.TotalRows);
            Assert.Equal(2, preview.ValidRows);
            var error = Assert.Single(preview.Errors);
            Assert.Equal(3, error.Line); // línea del archivo, contando el encabezado
            Assert.Equal(N("precio"), error.Column, ignoreCase: true);
            Assert.Equal(0, (await Db.Crud.ImportAsync(Conn, Schema, N("producto"), Csv(bad), Core.Transfer.TransferFormat.Csv, null, default)).Imported);
            Assert.Equal(0, await Count());

            // La base rechaza la tercera (código duplicado): se revierten las dos primeras.
            var dup = header + string.Join("\n", Line(prefix + "-1", "10"), Line(prefix + "-2", "20"), Line(prefix + "-1", "30"));
            var rejected = await Db.Crud.ImportAsync(Conn, Schema, N("producto"), Csv(dup), Core.Transfer.TransferFormat.Csv, null, default);
            Assert.Equal(0, rejected.Imported);
            Assert.Equal(4, Assert.Single(rejected.Errors).Line);
            Assert.Equal(0, await Count());

            // Todo bien: entran las tres.
            var ok = header + string.Join("\n", Line(prefix + "-1", "10"), Line(prefix + "-2", "20.5"), Line(prefix + "-3", "30"));
            var done = await Db.Crud.ImportAsync(Conn, Schema, N("producto"), Csv(ok), Core.Transfer.TransferFormat.Csv, null, default);
            Assert.Equal(3, done.Imported);
            Assert.Equal(3, await Count());

            // Una columna obligatoria sin asociar es un error general (línea 0).
            var sinCodigo = await Db.Crud.PreviewImportAsync(Conn, Schema, N("producto"), Csv(header + Line("x", "1")),
                Core.Transfer.TransferFormat.Csv, new Dictionary<string, string?> { [N("codigo")] = null }, default);
            Assert.Contains(sinCodigo.Errors, e => e.Line == 0 && string.Equals(e.Column, N("codigo"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            var rows = await Db.Crud.ListAsync(Conn, Schema, N("producto"),
                new ListRequest { PageSize = 200, Filters = new Dictionary<string, string> { [N("codigo")] = prefix } }, default);
            foreach (var r in rows.Rows)
                await Db.Crud.DeleteAsync(Conn, Schema, N("producto"), Key((N("id"), r[N("id")]!)), default);
        }
    }
    // ---------- Árboles (P-016) ----------

    [Fact]
    public async Task Arbol_por_autorreferencia_y_sin_ciclos()
    {
        var empleado = await TableAsync("empleado");
        Assert.Equal(N("jefe_id"), empleado.SelfReference!.Columns[0], ignoreCase: true);

        var roots = await Db.Crud.TreeAsync(Conn, Schema, N("empleado"), null, 100, default);
        var ana = Assert.Single(roots, n => n.Label == "Ana Gerente");
        Assert.True(ana.HasChildren);
        var hijos = await Db.Crud.TreeAsync(Conn, Schema, N("empleado"), Convert.ToString(ana.Value), 100, default);
        var luis = Assert.Single(hijos, n => n.Label == "Luis Vendedor");
        Assert.False(luis.HasChildren);

        // Ana bajo Luis (que depende de Ana) y Ana bajo sí misma: ciclos rechazados en el campo.
        foreach (var jefe in new[] { luis.Value, ana.Value })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.UpdateAsync(Conn, Schema, N("empleado"),
                Key((N("id"), ana.Value!)), Body((N("jefe_id"), Convert.ToInt64(jefe))), default));
            Assert.Contains("ciclo", ex.Errors.Single(e => string.Equals(e.Key, N("jefe_id"), StringComparison.OrdinalIgnoreCase)).Value);
        }

        // Un cambio válido sí se guarda: un empleado nuevo bajo Luis, y después se borra.
        var nuevo = await Db.Crud.CreateAsync(Conn, Schema, N("empleado"),
            Body((N("nombre"), "Temporal " + UniqueCode()), (N("jefe_id"), Convert.ToInt64(luis.Value))), default);
        try
        {
            Assert.True((await Db.Crud.TreeAsync(Conn, Schema, N("empleado"), Convert.ToString(ana.Value), 100, default))
                .Single(n => n.Label == "Luis Vendedor").HasChildren);
            await Db.Crud.UpdateAsync(Conn, Schema, N("empleado"), Key((N("id"), nuevo[N("id")]!)),
                Body((N("jefe_id"), Convert.ToInt64(ana.Value))), default);
        }
        finally
        {
            await Db.Crud.DeleteAsync(Conn, Schema, N("empleado"), Key((N("id"), nuevo[N("id")]!)), default);
        }
    }
    // ---------- Archivos (P-018) ----------

    [Fact]
    public async Task Archivo_se_sube_en_base64_se_descarga_y_se_quita()
    {
        var categoria = await TableAsync("categoria");
        var icono = categoria.FindColumn(N("icono"))!;
        Assert.True(icono.IsFile);
        Assert.False(icono.IsSelectable); // el contenido no va en el listado

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        var key = Key((N("id"), 2));
        try
        {
            var updated = await Db.Crud.UpdateAsync(Conn, Schema, N("categoria"), key,
                Body((N("icono"), "data:image/png;base64," + Convert.ToBase64String(png))), default);
            Assert.Equal(png.Length, Convert.ToInt32(updated[N("icono") + QueryBuilder.SizeSuffix]));
            Assert.False(updated.ContainsKey(N("icono")));

            var file = await Db.Crud.GetFileAsync(Conn, Schema, N("categoria"), key, N("icono"), default);
            Assert.Equal(png, file!.Content);
            Assert.Equal("image/png", file.ContentType);
            Assert.EndsWith(".png", file.FileName);

            // Con un tope de 1 MB, un archivo más grande se rechaza en el campo.
            var chica = new Core.Services.CrudService(Db.Schemas, new Core.Services.CrudOptions { FileMaxMegabytes = 1 });
            var ex = await Assert.ThrowsAsync<ValidationException>(() => chica.UpdateAsync(Conn, Schema, N("categoria"), key,
                Body((N("icono"), Convert.ToBase64String(new byte[1024 * 1024 + 1]))), default));
            Assert.Contains("MB", ex.Errors.Values.Single());
        }
        finally
        {
            var cleared = await Db.Crud.UpdateAsync(Conn, Schema, N("categoria"), key, Body((N("icono"), null)), default);
            Assert.Null(cleared[N("icono") + QueryBuilder.SizeSuffix]);
        }
        Assert.Null(await Db.Crud.GetFileAsync(Conn, Schema, N("categoria"), key, N("icono"), default));
    }
}
