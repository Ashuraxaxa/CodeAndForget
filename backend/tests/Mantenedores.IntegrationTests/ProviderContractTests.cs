using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
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
        Assert.Equal(10, result.Tables);
    }

    // ---------- Lectura del esquema ----------

    [Fact]
    public async Task Lee_tablas_vistas_y_tablas_sin_pk()
    {
        var schema = (await Db.Schemas.GetContextAsync(Conn, default)).Schema;
        Assert.Equal(10, schema.Tables.Count);

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
        var items = await Db.Crud.LookupAsync(Conn, Schema, N("categoria"), "beb", 20, default);
        var item = Assert.Single(items);
        Assert.Equal("Bebidas", item.Label);
        Assert.Equal(1L, Convert.ToInt64(item.Value));
    }

    // ---------- Escritura ----------

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
}
