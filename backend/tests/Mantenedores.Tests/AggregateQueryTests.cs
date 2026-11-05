using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.MySql;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.Tests;

/// <summary>Consultas de resumen de los dashboards (P-127, D-111).</summary>
public class AggregateQueryTests
{
    private readonly DatabaseSchema _schema = TestSchema.Build();

    /// <summary>Producto con una fecha de alta, para agrupar por período.</summary>
    private TableInfo Producto
    {
        get
        {
            var p = _schema.FindTable("ventas", "producto")!;
            return p with
            {
                Columns =
                [
                    .. p.Columns,
                    new ColumnInfo { Name = "creado", NativeType = "timestamptz", Type = LogicalType.DateTime, Ordinal = 7, HasTimeZone = true },
                    new ColumnInfo { Name = "oculta", NativeType = "int4", Type = LogicalType.Integer, Ordinal = 8, Hidden = true }
                ]
            };
        }
    }

    [Fact]
    public void Contar_por_fk_trae_el_texto_y_ordena_por_el_resultado()
    {
        var q = new QueryBuilder(new PostgresDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            GroupBy = [new AggregateGroup("categoria_id")],
            Order = AggregateOrder.ValueDesc,
            Limit = 10
        });

        Assert.Equal(
            "SELECT t0.\"categoria_id\" AS \"g0\", a0.\"nombre\" AS \"g0l\", COUNT(*) AS \"v\" " +
            "FROM \"ventas\".\"producto\" t0 LEFT JOIN \"ventas\".\"categoria\" a0 ON a0.\"id\" = t0.\"categoria_id\" " +
            "GROUP BY t0.\"categoria_id\", a0.\"nombre\" ORDER BY \"v\" DESC, \"g0\" LIMIT @p1 OFFSET @p0",
            q.Sql);
        Assert.Equal(11L, q.Parameters["p1"]); // uno más, para saber si había más grupos
    }

    [Fact]
    public void Sin_agrupar_es_un_solo_numero_con_los_filtros_de_la_grilla()
    {
        var q = new QueryBuilder(new PostgresDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            Function = AggregateFunction.Sum,
            Column = "precio",
            Filters = new Dictionary<string, string> { ["codigo"] = "BEB" }
        });

        Assert.Equal("SELECT SUM(t0.\"precio\") AS \"v\" FROM \"ventas\".\"producto\" t0 WHERE t0.\"codigo\" ILIKE @p0 ESCAPE '!'", q.Sql);
    }

    [Fact]
    public void Por_mes_con_rango_de_fechas_en_utc()
    {
        var q = new QueryBuilder(new PostgresDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            Function = AggregateFunction.Avg,
            Column = "categoria_id",
            GroupBy = [new AggregateGroup("creado", DateBucket.Month)],
            DateColumn = "creado",
            From = "2026-01-01T00:00:00Z",
            To = "2026-07-01T00:00:00Z"
        });

        Assert.Contains("CAST(date_trunc('month', (t0.\"creado\" AT TIME ZONE 'UTC')) AS date) AS \"g0\"", q.Sql);
        Assert.Contains("AVG(CAST(t0.\"categoria_id\" AS DECIMAL(28, 6)))", q.Sql); // un entero no se trunca al promediar
        Assert.Contains("WHERE t0.\"creado\" >= @p0 AND t0.\"creado\" < @p1", q.Sql);
        Assert.Contains("GROUP BY CAST(date_trunc('month', (t0.\"creado\" AT TIME ZONE 'UTC')) AS date) ORDER BY \"g0\"", q.Sql);
        Assert.DoesNotContain("LEFT JOIN", q.Sql); // por fecha no hay texto de FK
    }

    [Theory]
    [InlineData(DateBucket.Day, "CAST(SWITCHOFFSET(t0.[creado], '+00:00') AS date)")]
    [InlineData(DateBucket.Week, "DATEADD(day, DATEDIFF(day, CAST('19000101' AS date), CAST(SWITCHOFFSET(t0.[creado], '+00:00') AS date)) / 7 * 7, CAST('19000101' AS date))")]
    [InlineData(DateBucket.Month, "DATEFROMPARTS(YEAR(SWITCHOFFSET(t0.[creado], '+00:00')), MONTH(SWITCHOFFSET(t0.[creado], '+00:00')), 1)")]
    [InlineData(DateBucket.Year, "DATEFROMPARTS(YEAR(SWITCHOFFSET(t0.[creado], '+00:00')), 1, 1)")]
    public void Sql_server_2016_agrupa_por_fecha_sin_datetrunc(DateBucket bucket, string expected)
    {
        var q = new QueryBuilder(new SqlServerDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            GroupBy = [new AggregateGroup("creado", bucket)]
        });

        Assert.StartsWith($"SELECT {expected} AS [g0], COUNT(*) AS [v]", q.Sql);
        Assert.EndsWith("ORDER BY [g0] OFFSET @p0 ROWS FETCH NEXT @p1 ROWS ONLY", q.Sql);
        Assert.DoesNotContain("DATETRUNC", q.Sql);
    }

    [Fact]
    public void Mysql_agrupa_por_semana_desde_el_lunes_y_arma_la_tabla_dinamica()
    {
        var q = new QueryBuilder(new MySqlDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            Function = AggregateFunction.CountDistinct,
            Column = "codigo",
            GroupBy = [new AggregateGroup("creado", DateBucket.Week), new AggregateGroup("categoria_id")]
        });

        Assert.Equal(
            "SELECT DATE_SUB(DATE(t0.`creado`), INTERVAL WEEKDAY(t0.`creado`) DAY) AS `g0`, t0.`categoria_id` AS `g1`, " +
            "a1.`nombre` AS `g1l`, COUNT(DISTINCT t0.`codigo`) AS `v` " +
            "FROM `ventas`.`producto` t0 LEFT JOIN `ventas`.`categoria` a1 ON a1.`id` = t0.`categoria_id` " +
            "GROUP BY DATE_SUB(DATE(t0.`creado`), INTERVAL WEEKDAY(t0.`creado`) DAY), t0.`categoria_id`, a1.`nombre` " +
            "ORDER BY `g0`, `g1` LIMIT @p1 OFFSET @p0",
            q.Sql);
    }

    [Theory]
    [InlineData(AggregateFunction.Sum, "codigo")]      // texto
    [InlineData(AggregateFunction.Avg, "creado")]      // fecha
    [InlineData(AggregateFunction.Max, "notas")]       // texto largo
    [InlineData(AggregateFunction.Sum, "oculta")]      // oculta para el usuario
    [InlineData(AggregateFunction.Sum, "no_existe")]
    [InlineData(AggregateFunction.Sum, null)]          // falta la columna
    public void Rechaza_metricas_que_no_calzan(AggregateFunction function, string? column)
    {
        var qb = new QueryBuilder(new PostgresDialect());
        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, new AggregateRequest { Function = function, Column = column }));
    }

    [Fact]
    public void Rechaza_agrupaciones_que_no_calzan()
    {
        var qb = new QueryBuilder(new PostgresDialect());
        AggregateRequest By(params AggregateGroup[] g) => new() { GroupBy = g };

        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, By(new AggregateGroup("oculta"))));                 // oculta
        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, By(new AggregateGroup("foto"))));                   // archivo
        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, By(new AggregateGroup("codigo", DateBucket.Month)))); // no es fecha
        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, By(new AggregateGroup("codigo"), new AggregateGroup("categoria_id"), new AggregateGroup("precio"))));
        Assert.Throws<ValidationException>(() => qb.BuildAggregate(_schema, Producto, new AggregateRequest { DateColumn = "codigo", From = "2026-01-01" }));
    }

    [Fact]
    public void El_tope_de_grupos_no_pasa_de_500()
    {
        var q = new QueryBuilder(new PostgresDialect()).BuildAggregate(_schema, Producto, new AggregateRequest
        {
            GroupBy = [new AggregateGroup("codigo")],
            Limit = 100_000
        });
        Assert.Equal(501L, q.Parameters["p1"]);
    }
}
