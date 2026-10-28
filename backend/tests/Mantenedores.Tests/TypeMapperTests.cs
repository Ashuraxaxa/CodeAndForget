using Mantenedores.Core.Schema;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.Tests;

public class TypeMapperTests
{
    [Theory]
    [InlineData("int4", "b", "N", LogicalType.Integer)]
    [InlineData("numeric", "b", "N", LogicalType.Decimal)]
    [InlineData("varchar", "b", "S", LogicalType.String)]
    [InlineData("text", "b", "S", LogicalType.Text)]
    [InlineData("jsonb", "b", "U", LogicalType.Json)]
    [InlineData("uuid", "b", "U", LogicalType.Uuid)]
    [InlineData("estado", "e", "E", LogicalType.Enum)]
    [InlineData("_int4", "b", "A", LogicalType.Array)] // el elemento se resuelve aparte (P-018)
    [InlineData("geometry", "b", "U", LogicalType.ReadOnlyText)] // PostGIS, como WKT
    [InlineData("tsvector", "b", "U", LogicalType.Unsupported)]
    public void Postgres(string type, string kind, string category, LogicalType expected) =>
        Assert.Equal(expected, PostgresTypeMapper.Map(type, kind, category).Type);

    [Fact]
    public void Postgres_timestamptz_lleva_zona() =>
        Assert.True(PostgresTypeMapper.Map("timestamptz", "b", "D").HasTimeZone);

    [Theory]
    [InlineData("int", 4, LogicalType.Integer)]
    [InlineData("nvarchar", 100, LogicalType.String)]
    [InlineData("nvarchar", null, LogicalType.Text)]
    [InlineData("bit", 1, LogicalType.Boolean)]
    [InlineData("uniqueidentifier", 16, LogicalType.Uuid)]
    [InlineData("rowversion", 8, LogicalType.Binary)]
    [InlineData("geography", -1, LogicalType.Spatial)]
    [InlineData("hierarchyid", 892, LogicalType.ReadOnlyText)]
    [InlineData("sql_variant", 8016, LogicalType.Unsupported)]
    public void SqlServer(string type, int? length, LogicalType expected) =>
        Assert.Equal(expected, SqlServerTypeMapper.Map(type, length).Type);

    [Fact]
    public void SqlServer_largo_unicode_en_caracteres()
    {
        Assert.Equal(50, SqlServerTypeMapper.CharLength("nvarchar", 100));
        Assert.Equal(100, SqlServerTypeMapper.CharLength("varchar", 100));
        Assert.Null(SqlServerTypeMapper.CharLength("nvarchar", -1));
    }
}
