using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.Tests;

/// <summary>
/// Capacidades del dialecto del bloque M (P-020 a P-024), probadas con un dialecto de prueba que imita a un motor
/// sin RETURNING y con parámetros ":" (como MySQL u Oracle), para que Core no dependa de un motor concreto.
/// </summary>
public class DialectCapabilitiesTests
{
    private sealed class SinReturningDialect : ISqlDialect
    {
        public string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";
        public string ParameterPrefix => ":";
        public string Paging(string offsetParam, string limitParam) => $"LIMIT {limitParam} OFFSET {offsetParam}";
        public string Contains(string columnExpr, string param, bool isText = false) => $"{columnExpr} LIKE {param} ESCAPE '!'";
        public string? InsertReturningKeys(string table, IReadOnlyList<string> columns, IReadOnlyList<string> values,
            IReadOnlyList<ColumnInfo> keyColumns) => null;
        public string? LastInsertIdExpression => "LAST_INSERT_ID()";
    }

    private static readonly DatabaseSchema Schema = TestSchema.Build();
    private static TableInfo Producto => Schema.FindTable("ventas", "producto")!;

    private static TableInfo ConPk(ColumnInfo pk, params ColumnInfo[] others) => new()
    {
        Schema = "s",
        Name = "t",
        Columns = [pk with { IsPrimaryKey = true }, .. others],
        PrimaryKey = [pk.Name]
    };

    [Fact]
    public void Usa_el_prefijo_de_parametros_del_dialecto()
    {
        var (data, _) = new QueryBuilder(new SinReturningDialect()).BuildList(Schema, Producto,
            new ListRequest { Filters = new Dictionary<string, string> { ["categoria_id"] = "1" } });
        Assert.Contains("`categoria_id` = :p0", data.Sql);
        Assert.Contains("LIKE :p1", data.Sql); // también por la etiqueta (P-032)
        Assert.Contains("LIMIT :p3 OFFSET :p2", data.Sql);
        Assert.True(data.Parameters.ContainsKey("p0")); // en el diccionario, sin prefijo
    }

    [Fact]
    public void Sin_returning_y_pk_autonumerica_usa_el_ultimo_id()
    {
        var q = new QueryBuilder(new SinReturningDialect()).BuildInsert(Producto, new Dictionary<string, object?>
        {
            ["codigo"] = "A", ["categoria_id"] = 1, ["precio"] = 10m
        });
        Assert.Equal(
            "INSERT INTO `ventas`.`producto` (`codigo`, `categoria_id`, `precio`) VALUES (:p0, :p1, :p2); SELECT LAST_INSERT_ID() AS `id`",
            q.Query.Sql);
        Assert.Null(q.ProvidedKeys);
    }

    [Fact]
    public void Sin_returning_y_pk_ingresada_usa_los_valores_del_alta()
    {
        var table = ConPk(new ColumnInfo { Name = "codigo", NativeType = "varchar", Type = LogicalType.String },
            new ColumnInfo { Name = "nombre", NativeType = "varchar", Type = LogicalType.String, IsNullable = true });
        var q = new QueryBuilder(new SinReturningDialect()).BuildInsert(table,
            new Dictionary<string, object?> { ["codigo"] = "X1", ["nombre"] = "Uno" });
        Assert.Equal("INSERT INTO `s`.`t` (`codigo`, `nombre`) VALUES (:p0, :p1)", q.Query.Sql);
        Assert.Equal("X1", q.ProvidedKeys!["codigo"]);
    }

    [Fact]
    public void Sin_returning_y_pk_uuid_con_default_la_genera_la_app()
    {
        var table = ConPk(new ColumnInfo { Name = "id", NativeType = "char", Type = LogicalType.Uuid, HasDefault = true },
            new ColumnInfo { Name = "nombre", NativeType = "varchar", Type = LogicalType.String });
        var q = new QueryBuilder(new SinReturningDialect()).BuildInsert(table, new Dictionary<string, object?> { ["nombre"] = "x" });

        var id = Assert.IsType<Guid>(q.ProvidedKeys!["id"]);
        Assert.Equal("INSERT INTO `s`.`t` (`nombre`, `id`) VALUES (:p0, :p1)", q.Query.Sql);
        Assert.Equal(id, q.Query.Parameters["p1"]);
    }

    [Fact]
    public void Sin_returning_y_pk_no_uuid_con_default_pide_el_valor()
    {
        var table = ConPk(new ColumnInfo { Name = "codigo", NativeType = "varchar", Type = LogicalType.String, HasDefault = true },
            new ColumnInfo { Name = "nombre", NativeType = "varchar", Type = LogicalType.String });
        var ex = Assert.Throws<ValidationException>(() => new QueryBuilder(new SinReturningDialect())
            .BuildInsert(table, new Dictionary<string, object?> { ["nombre"] = "x" }));
        Assert.Contains("codigo", ex.Errors.Keys);
    }

    [Fact]
    public void Numeros_grandes_se_leen_y_escriben_como_texto()
    {
        var big = new ColumnInfo { Name = "monto", NativeType = "numeric", Type = LogicalType.Decimal, Precision = 38, IsLargeNumber = true };
        var table = ConPk(new ColumnInfo { Name = "id", NativeType = "int4", Type = LogicalType.Integer, IsIdentity = true, HasDefault = true }, big);
        var schema = SchemaAssembler.Assemble([], [], []) with { Tables = [table] };

        var pg = new QueryBuilder(new PostgresDialect());
        Assert.Contains("CAST(t0.\"monto\" AS text) AS \"monto\"", pg.BuildList(schema, table, new ListRequest()).Data.Sql);
        Assert.Contains("CAST(@p0 AS numeric)", pg.BuildInsert(table, new Dictionary<string, object?> { ["monto"] = "1" }).Query.Sql);

        var mssql = new QueryBuilder(new SqlServerDialect());
        Assert.Contains("CAST(t0.[monto] AS varchar(50)) AS [monto]", mssql.BuildList(schema, table, new ListRequest()).Data.Sql);

        const string huge = "1234567890123456789012345678901234.5678";
        Assert.Equal(huge, ValueConverter.FromString(big, huge)); // no pasa por decimal
        Assert.Throws<ValidationException>(() => ValueConverter.FromString(big, "12,5"));
    }

    [Fact]
    public void Columnas_no_ordenables_ni_filtrables_se_rechazan()
    {
        var xml = new ColumnInfo { Name = "doc", NativeType = "xml", Type = LogicalType.Json, IsNullable = true, Sortable = false, Filterable = false };
        var table = ConPk(new ColumnInfo { Name = "id", NativeType = "int", Type = LogicalType.Integer }, xml);
        var schema = SchemaAssembler.Assemble([], [], []) with { Tables = [table] };
        var qb = new QueryBuilder(new SqlServerDialect());

        Assert.False(xml.IsSortable);
        Assert.False(xml.IsFilterable);
        Assert.Throws<ValidationException>(() => qb.BuildList(schema, table, new ListRequest { Sort = [new SortSpec("doc")] }));
        Assert.Throws<ValidationException>(() => qb.BuildList(schema, table,
            new ListRequest { Filters = new Dictionary<string, string> { ["doc"] = "{}" } }));
    }

    [Fact]
    public void Los_textos_no_ordenables_igual_se_filtran_por_contiene()
    {
        var text = new ColumnInfo { Name = "notas", NativeType = "ntext", Type = LogicalType.Text, IsNullable = true, Sortable = false, Filterable = false };
        Assert.True(text.IsFilterable);
        Assert.False(text.IsSortable);
    }

    [Theory]
    [InlineData("100!", "%100!!%")]
    [InlineData("a_b", "%a!_b%")]
    [InlineData("[x]", "%![x]%")]
    public void El_escape_de_like_es_neutro(string input, string expected) =>
        Assert.Equal(expected, QueryBuilder.LikePattern(input));
}
