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
    [InlineData("geometry", "b", "U", LogicalType.Spatial)] // PostGIS, como WKT editable (P-104)
    [InlineData("tsvector", "b", "U", LogicalType.Unsupported)]
    [InlineData("inet", "b", "I", LogicalType.String)]
    [InlineData("int4range", "r", "R", LogicalType.String)]
    [InlineData("direccion", "c", "C", LogicalType.String)]
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

public class PostgisSridTests
{
    [Theory]
    [InlineData("geometry", "geometry(Point,4326)", 4326)]
    [InlineData("geography", "geography(Point, 4326)", 4326)]
    [InlineData("geography", "geography", 4326)]
    [InlineData("geometry", "geometry", 0)]
    public void Lee_el_srid_del_tipo(string type, string format, int expected) =>
        Assert.Equal(expected, Mantenedores.Providers.Postgres.PostgresTypeMapper.Srid(type, format));

    [Fact]
    public void Otros_tipos_no_tienen_srid() => Assert.Null(Mantenedores.Providers.Postgres.PostgresTypeMapper.Srid("int4", "integer"));
}
