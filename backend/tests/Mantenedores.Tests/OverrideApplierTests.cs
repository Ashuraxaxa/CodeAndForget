using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.Tests;

/// <summary>Ajustes por tabla y columna (P-011): se aplican sobre el esquema leído, sin tocar la base.</summary>
public class OverrideApplierTests
{
    private static readonly DatabaseSchema Raw = TestSchema.Build();

    private static DatabaseSchema Apply(string schema, string table, TableOverride o) =>
        OverrideApplier.Apply(Raw, new Dictionary<(string, string), TableOverride> { [(schema, table)] = o });

    private static Dictionary<string, ColumnOverride> Cols(params (string Name, ColumnOverride O)[] cols) =>
        cols.ToDictionary(c => c.Name, c => c.O);

    [Fact]
    public void Etiquetas_grupo_orden_y_visibilidad_cambian_la_tabla()
    {
        var s = Apply("VENTAS", "Producto", new TableOverride // la clave no distingue mayúsculas
        {
            Label = "Productos",
            Group = "Catálogo",
            DefaultSort = "-precio, codigo",
            Columns = Cols(
                ("precio", new ColumnOverride { Label = "Precio de lista", Order = 0, HelpText = "Con IVA" }),
                ("notas", new ColumnOverride { ShowInGrid = false, Widget = "TEXTAREA" }))
        });
        var t = s.FindTable("ventas", "producto")!;

        Assert.Equal("Productos", t.Label);
        Assert.Equal("Catálogo", t.Group);
        Assert.Equal("-precio,codigo", t.DefaultSort);
        Assert.Equal("precio", t.Columns[0].Name); // Order = 0 la pone primera
        Assert.Equal("Precio de lista", t.Columns[0].Label);
        Assert.Equal("Con IVA", t.Columns[0].HelpText);
        Assert.False(t.FindColumn("notas")!.ShowInGrid);
        Assert.Equal("textarea", t.FindColumn("notas")!.Widget);
        Assert.Empty(s.OverrideWarnings);
        Assert.Equal(Raw.Hash, s.Hash); // la huella sigue siendo la del catálogo
    }

    [Fact]
    public void Columna_oculta_no_se_lee_ni_se_escribe_ni_se_busca()
    {
        var s = Apply("ventas", "producto", new TableOverride { Columns = Cols(("notas", new ColumnOverride { Hidden = true })) });
        var t = s.FindTable("ventas", "producto")!;
        var notas = t.FindColumn("notas")!;

        Assert.False(notas.IsSelectable);
        Assert.True(notas.IsReadOnly);
        Assert.False(notas.IsFilterable);

        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(s, t, new ListRequest { Search = "x" });
        Assert.DoesNotContain("notas", data.Sql);
        Assert.Contains("codigo", data.Sql);
    }

    [Fact]
    public void Una_fk_oculta_no_trae_etiqueta()
    {
        var s = Apply("ventas", "producto", new TableOverride { Columns = Cols(("categoria_id", new ColumnOverride { Hidden = true, ReadOnly = true })) });
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(s, s.FindTable("ventas", "producto")!, new ListRequest());
        Assert.DoesNotContain("categoria", data.Sql);
        Assert.Equal(6, Raw.FindTable("ventas", "producto")!.FindColumn("notas")!.Order); // sin ajuste: la posición en la tabla
    }

    [Fact]
    public void La_pk_no_se_oculta_de_las_consultas_aunque_se_marque_oculta()
    {
        var s = Apply("ventas", "producto", new TableOverride { Columns = Cols(("id", new ColumnOverride { Hidden = true })) });
        var id = s.FindTable("ventas", "producto")!.FindColumn("id")!;
        Assert.True(id.Hidden); // la UI no la muestra…
        Assert.True(id.IsSelectable); // …pero se sigue leyendo como clave
    }

    [Fact]
    public void Columna_a_mostrar_por_ajuste_y_vuelve_a_elegirse_si_la_automatica_queda_oculta()
    {
        var elegida = Apply("ventas", "producto", new TableOverride { DisplayColumn = "CODIGO" });
        Assert.Equal("codigo", elegida.FindTable("ventas", "producto")!.DisplayColumn);

        var oculta = Apply("ventas", "categoria", new TableOverride { Columns = Cols(("nombre", new ColumnOverride { Hidden = true })) });
        Assert.Equal("id", oculta.FindTable("ventas", "categoria")!.DisplayColumn);
    }

    [Fact]
    public void Solo_lectura_y_obligatoria_por_ajuste()
    {
        var s = Apply("ventas", "producto", new TableOverride
        {
            Columns = Cols(("precio", new ColumnOverride { ReadOnly = true }), ("notas", new ColumnOverride { Required = true }))
        });
        var t = s.FindTable("ventas", "producto")!;
        Assert.True(t.FindColumn("precio")!.IsReadOnly);
        Assert.False(t.FindColumn("notas")!.IsNullable);
        Assert.True(t.FindColumn("notas")!.IsRequiredOnInsert);

        var ro = Apply("ventas", "producto", new TableOverride { ReadOnly = true }).FindTable("ventas", "producto")!;
        Assert.False(ro.IsEditable);
    }

    [Fact]
    public void Pk_y_fk_virtuales_habilitan_edicion_etiquetas_y_referencias()
    {
        var raw = ConPrecioHistorico();
        var s = OverrideApplier.Apply(raw, new Dictionary<(string, string), TableOverride>
        {
            [("ventas", "precio_historico")] = new()
            {
                VirtualPrimaryKey = ["producto_id", "desde"],
                VirtualForeignKeys = [new VirtualForeignKey(["producto_id"], "ventas", "producto", ["id"])]
            }
        });
        var t = s.FindTable("ventas", "precio_historico")!;

        Assert.False(raw.FindTable("ventas", "precio_historico")!.IsEditable);
        Assert.True(t.IsEditable);
        Assert.True(t.HasVirtualPrimaryKey);
        Assert.Equal(["producto_id", "desde"], t.PrimaryKey);
        Assert.True(t.FindColumn("desde")!.IsPrimaryKey);

        var fk = Assert.Single(t.ForeignKeys);
        Assert.Equal("vfk_precio_historico_producto_id", fk.Name);
        Assert.Contains(s.FindTable("ventas", "producto")!.ReferencedBy, r => r.Table == "precio_historico");

        // La FK virtual da etiqueta en el listado como una declarada.
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(s, t, new ListRequest());
        Assert.Contains("LEFT JOIN \"ventas\".\"producto\" j0 ON j0.\"id\" = t0.\"producto_id\"", data.Sql);
    }

    [Fact]
    public void Lo_que_no_se_puede_aplicar_queda_como_aviso_y_el_resto_se_aplica()
    {
        var s = OverrideApplier.Apply(Raw, new Dictionary<(string, string), TableOverride>
        {
            [("ventas", "producto")] = new()
            {
                Label = "Productos",
                DisplayColumn = "no_existe",
                Columns = Cols(("borrada", new ColumnOverride { Label = "x" }), ("codigo", new ColumnOverride { Pattern = "[" }))
            },
            [("ventas", "tabla_borrada")] = new() { Label = "x" }
        });

        Assert.Equal("Productos", s.FindTable("ventas", "producto")!.Label);
        Assert.Null(s.FindTable("ventas", "producto")!.FindColumn("codigo")!.Pattern);
        Assert.Equal(4, s.OverrideWarnings.Count);
        Assert.Contains(s.OverrideWarnings, w => w.Contains("tabla_borrada") && w.Contains("no existe"));
        Assert.Contains(s.OverrideWarnings, w => w.Contains("columns.borrada"));
    }

    [Fact]
    public void Validar_devuelve_un_error_por_campo()
    {
        var ex = Assert.Throws<ValidationException>(() => OverrideApplier.Validate(Raw, "ventas", "producto", new TableOverride
        {
            DefaultSort = "foto", // binaria: no se ordena
            VirtualPrimaryKey = ["codigo"], // ya tiene PK
            Columns = Cols(
                ("precio", new ColumnOverride { Min = 10, Max = 1, Pattern = "x" }),
                ("codigo", new ColumnOverride { Hidden = true, Widget = "slider", Options = ["A", "B"] }))
        }));

        Assert.Contains("defaultSort", ex.Errors.Keys);
        Assert.Contains("virtualPrimaryKey", ex.Errors.Keys);
        Assert.Contains("columns.precio.min", ex.Errors.Keys);
        Assert.Contains("columns.precio.pattern", ex.Errors.Keys);
        Assert.Contains("columns.codigo.hidden", ex.Errors.Keys); // obligatoria y sin default
        Assert.Contains("columns.codigo.widget", ex.Errors.Keys);
        Assert.DoesNotContain("columns.codigo.options", ex.Errors.Keys);

        Assert.Throws<NotFoundException>(() => OverrideApplier.Validate(Raw, "ventas", "nada", new TableOverride()));
        OverrideApplier.Validate(Raw, "ventas", "producto", new TableOverride { Label = "ok" }); // no lanza
    }

    [Fact]
    public void Fk_virtual_invalida()
    {
        var raw = ConPrecioHistorico();
        void Bad(VirtualForeignKey v, string expected)
        {
            var ex = Assert.Throws<ValidationException>(() => OverrideApplier.Validate(raw, "ventas", "precio_historico",
                new TableOverride { VirtualForeignKeys = [v] }));
            Assert.Contains(expected, ex.Errors["virtualForeignKeys.0"]);
        }

        Bad(new(["producto_id"], "ventas", "no_existe", ["id"]), "no existe");
        Bad(new(["producto_id"], "ventas", "producto", ["id", "codigo"]), "misma cantidad");
        Bad(new(["nada"], "ventas", "producto", ["id"]), "'nada' no existe");
    }

    [Fact]
    public void El_orden_por_defecto_se_usa_si_no_se_pide_otro()
    {
        var s = Apply("ventas", "producto", new TableOverride { DefaultSort = "-precio" });
        var t = s.FindTable("ventas", "producto")!;
        var qb = new QueryBuilder(new PostgresDialect());

        Assert.Contains("ORDER BY t0.\"precio\" DESC, t0.\"id\" ASC", qb.BuildList(s, t, new ListRequest()).Data.Sql);
        Assert.Contains("ORDER BY t0.\"codigo\" ASC, t0.\"id\" ASC",
            qb.BuildList(s, t, new ListRequest { Sort = [new SortSpec("codigo")] }).Data.Sql);
    }

    [Fact]
    public void Reglas_de_escritura_patron_opciones_rango_y_obligatoria()
    {
        var s = Apply("ventas", "producto", new TableOverride
        {
            Columns = Cols(
                ("codigo", new ColumnOverride { Pattern = "[A-Z]{3}-\\d+", MaxLength = 8 }),
                ("notas", new ColumnOverride { Options = ["alta", "baja"], Required = true }),
                ("precio", new ColumnOverride { Min = 0, Max = 1000 }))
        });
        var t = s.FindTable("ventas", "producto")!;
        ColumnInfo C(string n) => t.FindColumn(n)!;
        JsonElement J(string json) => JsonDocument.Parse(json).RootElement;
        string Error(string col, string json) =>
            Assert.Throws<ValidationException>(() => ValueConverter.ForWrite(C(col), J(json))).Errors[col];

        Assert.Equal("ABC-12", ValueConverter.ForWrite(C("codigo"), J("\"ABC-12\"")));
        Assert.Equal("no tiene el formato esperado", Error("codigo", "\"xABC-12\"")); // patrón completo
        Assert.Equal("máximo 8 caracteres", Error("codigo", "\"ABC-12345\"")); // más corto que la BD (20)
        Assert.Equal(8, C("codigo").MaxLength);

        Assert.Equal("alta", ValueConverter.ForWrite(C("notas"), J("\"alta\"")));
        Assert.Contains("valor permitido", Error("notas", "\"media\""));
        Assert.Equal("es obligatorio", Error("notas", "\"  \""));
        Assert.Equal("es obligatorio", Error("notas", "null"));

        Assert.Equal(1000m, ValueConverter.ForWrite(C("precio"), J("1000")));
        Assert.Equal("mínimo 0", Error("precio", "-1"));
        Assert.Equal("máximo 1000", Error("precio", "1000.01"));

        // Los filtros no aplican reglas: se puede buscar un valor fuera de rango.
        Assert.Equal(-1m, ValueConverter.FromString(C("precio"), "-1"));
    }

    [Fact]
    public void Un_patron_catastrofico_se_corta_por_tiempo()
    {
        var col = new ColumnInfo { Name = "c", NativeType = "varchar", Type = LogicalType.String, Pattern = "(a+)+$" };
        var ex = Assert.Throws<ValidationException>(() =>
            ValueConverter.ForWrite(col, JsonDocument.Parse($"\"{new string('a', 5000)}!\"").RootElement));
        Assert.Equal("no tiene el formato esperado", ex.Errors["c"]);
    }

    [Fact]
    public void El_documento_json_usa_los_nombres_de_la_api()
    {
        var json = """
            { "label": "Productos", "virtualPrimaryKey": ["a"],
              "virtualForeignKeys": [{ "columns": ["a"], "refSchema": "s", "refTable": "t", "refColumns": ["id"] }],
              "columns": { "precio": { "min": 0, "showInGrid": false } } }
            """;
        var o = JsonSerializer.Deserialize<TableOverride>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Productos", o.Label);
        Assert.Equal("t", o.VirtualForeignKeys![0].RefTable);
        Assert.Equal(0m, o.Columns!["precio"].Min);
        Assert.False(o.Columns["precio"].ShowInGrid);
    }

    [Fact]
    public void Hijas_por_defecto_todas_y_por_ajuste_las_elegidas_en_orden()
    {
        var raw = ConPrecioHistorico();
        var vfk = new VirtualForeignKey(["producto_id"], "ventas", "producto", ["id"], "vfk_historico");
        var s = OverrideApplier.Apply(raw, new Dictionary<(string, string), TableOverride>
        {
            [("ventas", "precio_historico")] = new() { VirtualForeignKeys = [vfk] }
        });
        Assert.Equal(["vfk_historico"], s.FindTable("ventas", "producto")!.Children!.Select(f => f.Name));
        Assert.Equal(["producto_categoria_fk"], s.FindTable("ventas", "categoria")!.Children!.Select(f => f.Name));

        s = OverrideApplier.Apply(raw, new Dictionary<(string, string), TableOverride>
        {
            [("ventas", "precio_historico")] = new() { VirtualForeignKeys = [vfk] },
            [("ventas", "producto")] = new() { Children = ["no_existe", "vfk_historico"] },
            [("ventas", "categoria")] = new() { Children = [] }
        });
        Assert.Equal(["vfk_historico"], s.FindTable("ventas", "producto")!.Children!.Select(f => f.Name));
        Assert.Empty(s.FindTable("ventas", "categoria")!.Children!);
        Assert.Single(s.FindTable("ventas", "categoria")!.ReferencedBy); // ReferencedBy no cambia
        Assert.Contains(s.OverrideWarnings, w => w.Contains("children") && w.Contains("no_existe"));
    }

    [Fact]
    public void Filtro_exacto_por_columna_incluso_texto()
    {
        var t = Raw.FindTable("ventas", "producto")!;
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(Raw, t, new ListRequest
        {
            Exact = new Dictionary<string, string> { ["codigo"] = "BEB-001", ["categoria_id"] = "2" }
        });
        Assert.Contains("WHERE t0.\"codigo\" = @p0 AND t0.\"categoria_id\" = @p1", data.Sql);
        Assert.Equal("BEB-001", data.Parameters["p0"]);

        var oculta = Apply("ventas", "producto", new TableOverride { Columns = Cols(("notas", new ColumnOverride { Hidden = true })) });
        Assert.Throws<ValidationException>(() => new QueryBuilder(new PostgresDialect()).BuildList(oculta, oculta.FindTable("ventas", "producto")!,
            new ListRequest { Exact = new Dictionary<string, string> { ["notas"] = "x" } }));
        Assert.Throws<ValidationException>(() => new QueryBuilder(new PostgresDialect()).BuildList(Raw, t,
            new ListRequest { Exact = new Dictionary<string, string> { ["foto"] = "x" } }));
    }

    /// <summary>TestSchema + una tabla sin PK ni FK declaradas.</summary>
    private static DatabaseSchema ConPrecioHistorico()
    {
        ColumnInfo Col(string n, LogicalType t, int o) => new() { Name = n, NativeType = "x", Type = t, Ordinal = o };
        var extra = new TableInfo
        {
            Schema = "ventas", Name = "precio_historico", Kind = TableKind.Table,
            Columns = [Col("producto_id", LogicalType.Integer, 1), Col("desde", LogicalType.Date, 2), Col("precio", LogicalType.Decimal, 3)]
        };
        return Raw with { Tables = [.. Raw.Tables, extra] };
    }
}

public class OverrideLanguageTests
{
    [Fact]
    public void Nombres_y_ayudas_por_idioma()
    {
        // P-102: textos en otros idiomas para la tabla y sus columnas; los vacíos se descartan.
        var raw = TestSchema.Build();
        var o = new Mantenedores.Core.Overrides.TableOverride
        {
            Label = "Productos",
            Labels = new Dictionary<string, string> { ["en"] = "Products", ["RU"] = "Товары", ["fr"] = " " },
            Columns = new Dictionary<string, Mantenedores.Core.Overrides.ColumnOverride>
            {
                ["precio"] = new() { Labels = new Dictionary<string, string> { ["en"] = "Price" }, HelpTexts = new Dictionary<string, string> { ["en"] = "With VAT" } }
            }
        };
        var t = Mantenedores.Core.Overrides.OverrideApplier.Apply(raw, new Dictionary<(string, string), Mantenedores.Core.Overrides.TableOverride> { [("ventas", "producto")] = o })
            .FindTable("ventas", "producto")!;
        Assert.Equal(new Dictionary<string, string> { ["en"] = "Products", ["ru"] = "Товары" }, t.Labels);
        Assert.Equal("Price", t.FindColumn("precio")!.Labels!["en"]);
        Assert.Equal("With VAT", t.FindColumn("precio")!.HelpTexts!["en"]);

        var ex = Assert.Throws<Mantenedores.Core.Errors.ValidationException>(() => Mantenedores.Core.Overrides.OverrideApplier.Validate(raw, "ventas", "producto",
            new Mantenedores.Core.Overrides.TableOverride { Labels = new Dictionary<string, string> { ["inglés"] = "x" } }));
        Assert.Contains("labels", ex.Errors.Keys);
    }
}
