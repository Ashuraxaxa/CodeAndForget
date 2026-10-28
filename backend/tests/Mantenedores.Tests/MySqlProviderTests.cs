using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Providers.MySql;

namespace Mantenedores.Tests;

public class MySqlProviderTests
{
    [Theory]
    [InlineData("tinyint", "tinyint(1)", null, LogicalType.Boolean)]
    [InlineData("bit", "bit(1)", null, LogicalType.Boolean)]
    [InlineData("tinyint", "tinyint", null, LogicalType.Integer)]
    [InlineData("int", "int unsigned", null, LogicalType.Integer)]
    [InlineData("bigint", "bigint unsigned", null, LogicalType.Decimal)]   // no cabe en long
    [InlineData("char", "char(36)", 36, LogicalType.Uuid)]
    [InlineData("char", "char(2)", 2, LogicalType.String)]
    [InlineData("longtext", "longtext", null, LogicalType.Text)]
    [InlineData("json", "json", null, LogicalType.Json)]
    [InlineData("enum", "enum('a','b')", null, LogicalType.Enum)]
    [InlineData("timestamp", "timestamp", null, LogicalType.DateTime)]
    [InlineData("geometry", "geometry", null, LogicalType.Spatial)]
    [InlineData("point", "point", null, LogicalType.Spatial)]
    public void Mapea_tipos(string dataType, string columnType, int? length, LogicalType expected) =>
        Assert.Equal(expected, MySqlTypeMapper.Map(dataType, columnType, length));

    [Fact]
    public void Lee_los_valores_del_enum_incluidas_comillas() =>
        Assert.Equal(["a", "b'c", "d"], MySqlTypeMapper.EnumValues("enum('a','b''c','d')"));

    [Theory]
    [InlineData("tinyint", "tinyint", "tinyint signed")] // -128..127, no el 0..255 de SQL Server
    [InlineData("int", "int unsigned", "int unsigned")]  // hasta 4.294.967.295: no cabe en int
    [InlineData("int", "int", "int")]
    public void El_tipo_nativo_evita_rangos_de_otro_motor(string dataType, string columnType, string expected) =>
        Assert.Equal(expected, MySqlTypeMapper.NativeType(dataType, columnType));

    [Fact]
    public void Un_int_unsigned_grande_no_se_rechaza()
    {
        var col = new ColumnInfo { Name = "n", NativeType = "int unsigned", Type = LogicalType.Integer };
        Assert.Equal(4_000_000_000L, ValueConverter.FromString(col, "4000000000"));
    }

    [Fact]
    public void Dialecto_mysql()
    {
        var d = new MySqlDialect();
        Assert.Equal("`a``b`", d.Quote("a`b"));
        Assert.Null(d.InsertReturningKeys("t", [], [], []));
        Assert.Equal("INSERT INTO t () VALUES ()", d.Insert("t", [], []));
        Assert.Equal("LAST_INSERT_ID()", d.LastInsertIdExpression);
        Assert.Equal("CAST(c AS CHAR) LIKE @p0 ESCAPE '!'", d.Contains("c", "@p0"));
    }
}
