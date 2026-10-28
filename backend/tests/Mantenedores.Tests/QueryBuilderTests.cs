using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.Tests;

public class QueryBuilderTests
{
    private readonly Core.Schema.DatabaseSchema _schema = TestSchema.Build();
    private Core.Schema.TableInfo Producto => _schema.FindTable("ventas", "producto")!;

    [Fact]
    public void Listado_postgres_con_etiqueta_de_fk_filtros_y_paginacion()
    {
        var qb = new QueryBuilder(new PostgresDialect());
        var (data, count) = qb.BuildList(_schema, Producto, new ListRequest
        {
            Page = 3,
            PageSize = 10,
            Sort = [new SortSpec("precio", Descending: true)],
            Filters = new Dictionary<string, string> { ["codigo"] = "be_", ["categoria_id"] = "2" }
        });

        Assert.Equal(
            "SELECT t0.\"id\", t0.\"codigo\", t0.\"categoria_id\", t0.\"precio\", t0.\"notas\", j0.\"nombre\" AS \"__lbl0\", " +
            "OCTET_LENGTH(t0.\"foto\") AS \"__sz1\" " +
            "FROM \"ventas\".\"producto\" t0 LEFT JOIN \"ventas\".\"categoria\" j0 ON j0.\"id\" = t0.\"categoria_id\" " +
            "WHERE t0.\"codigo\" ILIKE @p0 ESCAPE '!' AND (t0.\"categoria_id\" = @p1 OR " +
            "t0.\"categoria_id\" IN (SELECT lf.\"id\" FROM \"ventas\".\"categoria\" lf WHERE lf.\"nombre\" ILIKE @p2 ESCAPE '!')) " +
            "ORDER BY t0.\"precio\" DESC, t0.\"id\" ASC LIMIT @p4 OFFSET @p3",
            data.Sql);
        Assert.Equal("%be!_%", data.Parameters["p0"]);
        Assert.Equal("categoria_id__label", data.Renames!["__lbl0"]); // alias corto → nombre final (P-021)
        Assert.Equal(2, data.Parameters["p1"]); // int4 → int
        Assert.Equal("%2%", data.Parameters["p2"]); // el mismo texto, buscado en la etiqueta (P-032)
        Assert.Equal(20L, data.Parameters["p3"]);
        Assert.Equal(10L, data.Parameters["p4"]);
        Assert.StartsWith("SELECT COUNT(*) FROM \"ventas\".\"producto\" t0 WHERE", count.Sql);
    }

    [Fact]
    public void Listado_sqlserver_usa_offset_fetch_y_corchetes()
    {
        var (data, _) = new QueryBuilder(new SqlServerDialect()).BuildList(_schema, Producto, new ListRequest());
        Assert.Contains("FROM [ventas].[producto] t0", data.Sql);
        Assert.EndsWith("ORDER BY t0.[id] ASC OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY", data.Sql);
    }

    [Fact]
    public void Las_columnas_binarias_no_se_seleccionan_solo_su_tamano()
    {
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(_schema, Producto, new ListRequest());
        Assert.DoesNotContain("t0.\"foto\",", data.Sql); // el contenido no viaja en el listado (P-018)
        Assert.Contains("OCTET_LENGTH(t0.\"foto\") AS \"__sz1\"", data.Sql);
        Assert.Equal("foto" + QueryBuilder.SizeSuffix, data.Renames!["__sz1"]);
        Assert.Contains("DATALENGTH(t0.[foto])", new QueryBuilder(new SqlServerDialect()).BuildList(_schema, Producto, new ListRequest()).Data.Sql);
    }

    [Fact]
    public void Busqueda_general_sobre_columnas_de_texto()
    {
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(_schema, Producto, new ListRequest { Search = "50%" });
        Assert.Contains("(t0.\"codigo\" ILIKE @p0 ESCAPE '!' OR t0.\"notas\" ILIKE @p0 ESCAPE '!')", data.Sql);
        Assert.Equal("%50!%%", data.Parameters["p0"]);
    }

    [Theory]
    [InlineData("precio; DROP TABLE x")]
    [InlineData("\"precio\"")]
    [InlineData("no_existe")]
    public void Rechaza_columnas_que_no_estan_en_la_metadata(string column)
    {
        var qb = new QueryBuilder(new PostgresDialect());
        Assert.Throws<ValidationException>(() =>
            qb.BuildList(_schema, Producto, new ListRequest { Sort = [new SortSpec(column)] }));
        Assert.Throws<ValidationException>(() =>
            qb.BuildList(_schema, Producto, new ListRequest { Filters = new Dictionary<string, string> { [column] = "1" } }));
    }

    [Fact]
    public void Los_identificadores_se_escapan()
    {
        Assert.Equal("\"a\"\"b\"", new PostgresDialect().Quote("a\"b"));
        Assert.Equal("[a]]b]", new SqlServerDialect().Quote("a]b"));
    }

    [Fact]
    public void Insert_omite_identity_y_exige_obligatorios()
    {
        var qb = new QueryBuilder(new PostgresDialect());
        var ex = Assert.Throws<ValidationException>(() =>
            qb.BuildInsert(Producto, new Dictionary<string, object?> { ["codigo"] = "A" }));
        Assert.Contains("categoria_id", ex.Errors.Keys);
        Assert.Contains("precio", ex.Errors.Keys);

        var q = qb.BuildInsert(Producto, new Dictionary<string, object?>
        {
            ["id"] = 99, ["codigo"] = "A", ["categoria_id"] = 1, ["precio"] = 10m
        });
        Assert.Equal(
            "INSERT INTO \"ventas\".\"producto\" (\"codigo\", \"categoria_id\", \"precio\") VALUES (@p0, @p1, @p2) RETURNING \"id\"",
            q.Query.Sql);
        Assert.Null(q.ProvidedKeys); // la clave la devuelve el motor
    }

    [Fact]
    public void Insert_sqlserver_usa_output_into_para_tolerar_triggers()
    {
        var q = new QueryBuilder(new SqlServerDialect()).BuildInsert(Producto, new Dictionary<string, object?>
        {
            ["codigo"] = "A", ["categoria_id"] = 1, ["precio"] = 10m
        });
        Assert.StartsWith("DECLARE @__keys TABLE ([id] int4);", q.Query.Sql);
        Assert.Contains("OUTPUT INSERTED.[id] INTO @__keys VALUES", q.Query.Sql);
        Assert.EndsWith("SELECT * FROM @__keys;", q.Query.Sql);
    }

    [Fact]
    public void Update_no_toca_la_pk_y_filtra_por_ella()
    {
        var q = new QueryBuilder(new PostgresDialect()).BuildUpdate(Producto,
            new Dictionary<string, object?> { ["id"] = 5 },
            new Dictionary<string, object?> { ["id"] = 7, ["precio"] = 20m });
        Assert.Equal("UPDATE \"ventas\".\"producto\" SET \"precio\" = @p0 WHERE \"id\" = @p1", q.Sql);
        Assert.Equal(5, q.Parameters["p1"]);
    }

    [Fact]
    public void Las_vistas_no_aceptan_escritura()
    {
        var view = _schema.FindTable("ventas", "v_productos")!;
        var qb = new QueryBuilder(new PostgresDialect());
        Assert.Throws<OperationNotAllowedException>(() => qb.BuildInsert(view, new Dictionary<string, object?>()));
        Assert.Throws<OperationNotAllowedException>(() => qb.BuildDelete(view, new Dictionary<string, object?>()));
    }

    [Fact]
    public void Lookup_devuelve_pk_y_columna_a_mostrar()
    {
        var categoria = _schema.FindTable("ventas", "categoria")!;
        var q = new QueryBuilder(new PostgresDialect()).BuildLookup(categoria, "beb", 20);
        Assert.Equal(
            "SELECT t0.\"id\" AS \"value\", t0.\"nombre\" AS \"label\" FROM \"ventas\".\"categoria\" t0 " +
            "WHERE t0.\"nombre\" ILIKE @p0 ESCAPE '!' ORDER BY t0.\"nombre\" LIMIT @p2 OFFSET @p1",
            q.Sql);
    }

    [Fact]
    public void El_tamano_de_pagina_tiene_tope()
    {
        var (data, _) = new QueryBuilder(new PostgresDialect()).BuildList(_schema, Producto, new ListRequest { PageSize = 100_000 });
        Assert.Equal((long)ListRequest.MaxPageSize, data.Parameters["p1"]);
    }

    [Fact]
    public void Arbol_por_niveles_con_raices_huerfanas_y_aviso_de_hijos()
    {
        var empleado = Arbol();
        var qb = new QueryBuilder(new PostgresDialect());

        var roots = qb.BuildTreeLevel(empleado, null, 50);
        Assert.Equal(
            "SELECT t0.\"id\" AS \"value\", t0.\"nombre\" AS \"label\", " +
            "CASE WHEN EXISTS (SELECT 1 FROM \"rrhh\".\"empleado\" c WHERE c.\"jefe_id\" = t0.\"id\") THEN 1 ELSE 0 END AS \"children\" " +
            "FROM \"rrhh\".\"empleado\" t0 WHERE t0.\"jefe_id\" IS NULL OR NOT EXISTS (SELECT 1 FROM \"rrhh\".\"empleado\" p WHERE p.\"id\" = t0.\"jefe_id\") " +
            "ORDER BY t0.\"nombre\" LIMIT @p1 OFFSET @p0",
            roots.Sql);

        var children = qb.BuildTreeLevel(empleado, 7, 50);
        Assert.Contains("WHERE t0.\"jefe_id\" = @p0", children.Sql);
        Assert.Equal(7, children.Parameters["p0"]);

        Assert.Throws<OperationNotAllowedException>(() => qb.BuildTreeLevel(Producto, null, 50));
    }

    private static Core.Schema.TableInfo Arbol()
    {
        Core.Schema.ColumnInfo C(string n, Core.Schema.LogicalType t, int o, bool identity = false, bool nullable = false) =>
            new() { Name = n, NativeType = "int4", Type = t, Ordinal = o, IsIdentity = identity, HasDefault = identity, IsNullable = nullable };
        var raw = Core.Schema.SchemaAssembler.Assemble(
            [new Core.Schema.RawTable("rrhh", "empleado", Core.Schema.TableKind.Table)],
            [
                new Core.Schema.RawColumn("rrhh", "empleado", C("id", Core.Schema.LogicalType.Integer, 1, identity: true)),
                new Core.Schema.RawColumn("rrhh", "empleado", C("nombre", Core.Schema.LogicalType.String, 2)),
                new Core.Schema.RawColumn("rrhh", "empleado", C("jefe_id", Core.Schema.LogicalType.Integer, 3, nullable: true))
            ],
            [
                new Core.Schema.RawKeyColumn("empleado_pk", Core.Schema.KeyKind.PrimaryKey, "rrhh", "empleado", "id", 1),
                new Core.Schema.RawKeyColumn("empleado_jefe_fk", Core.Schema.KeyKind.ForeignKey, "rrhh", "empleado", "jefe_id", 1, "rrhh", "empleado", "id")
            ]);
        var s = Core.Overrides.OverrideApplier.Apply(raw, new Dictionary<(string, string), Core.Overrides.TableOverride>());
        var t = s.FindTable("rrhh", "empleado")!;
        Assert.Equal("jefe_id", t.SelfReference!.Columns[0]);
        return t;
    }
}
