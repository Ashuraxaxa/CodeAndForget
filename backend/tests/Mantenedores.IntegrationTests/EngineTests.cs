using Mantenedores.Core.Errors;
using Mantenedores.Core.Schema;
using Mantenedores.IntegrationTests.Infrastructure;

namespace Mantenedores.IntegrationTests;

/// <summary>PostgreSQL: batería común + casos propios del motor (enum, jsonb).</summary>
public sealed class PostgresTests(PostgresFixture db) : ProviderContractTests<PostgresFixture>(db)
{
    [Fact]
    public async Task Enum_lista_sus_valores_y_valida()
    {
        var pedido = (await Db.Schemas.GetTableAsync(DatabaseFixture.ConnectionId, Schema, "pedido", default)).Table;
        var estado = pedido.FindColumn("estado")!;
        Assert.Equal(LogicalType.Enum, estado.Type);
        Assert.Equal(["borrador", "confirmado", "despachado", "anulado"], estado.EnumValues);

        await Assert.ThrowsAsync<ValidationException>(() => Db.Crud.CreateAsync(DatabaseFixture.ConnectionId, Schema, "pedido",
            Body(("cliente_id", Guid.Empty), ("estado", "zzz")), default));

        // El enum se creó después de la primera conexión de Npgsql (al cargar la base): igual se lee.
        var row = await Db.Crud.GetAsync(DatabaseFixture.ConnectionId, Schema, "pedido",
            new Dictionary<string, string> { ["id"] = "1" }, default);
        Assert.Equal("confirmado", row["estado"]);
    }

    [Fact]
    public async Task Jsonb_se_guarda_y_se_lee()
    {
        var created = await Db.Crud.CreateAsync(DatabaseFixture.ConnectionId, Schema, "producto", Body(
            ("codigo", UniqueCode()), ("nombre", "Con JSON"), ("categoria_id", 1), ("precio", 1),
            ("atributos", new { vegano = true })), default);
        Assert.Contains("vegano", Convert.ToString(created["atributos"]));
    }
}

/// <summary>SQL Server: batería común + casos propios del motor (columnas calculadas, rowversion).</summary>
public sealed class SqlServerTests(SqlServerFixture db) : ProviderContractTests<SqlServerFixture>(db)
{
    [Fact]
    public async Task Calculadas_y_rowversion_son_de_solo_lectura_y_se_ignoran_al_escribir()
    {
        var producto = (await Db.Schemas.GetTableAsync(DatabaseFixture.ConnectionId, Schema, "Producto", default)).Table;
        Assert.True(producto.FindColumn("PrecioConIva")!.IsComputed);
        Assert.True(producto.FindColumn("RowVer")!.IsRowVersion);
        Assert.False(producto.FindColumn("RowVer")!.IsSelectable);

        var created = await Db.Crud.CreateAsync(DatabaseFixture.ConnectionId, Schema, "Producto", Body(
            ("Codigo", UniqueCode()), ("Nombre", "Calculada"), ("CategoriaId", 1), ("Precio", 100), ("PrecioConIva", 1)), default);
        Assert.Equal(119m, Convert.ToDecimal(created["PrecioConIva"]));
    }
}

/// <summary>Casos propios de la familia MySQL: ENUM por columna, JSON, UUID en CHAR(36), TIMESTAMP en UTC.</summary>
public abstract class MySqlFamilyTests<TFixture>(TFixture db) : ProviderContractTests<TFixture>(db)
    where TFixture : MySqlFamilyFixture
{
    [Fact]
    public async Task Enum_de_columna_lista_sus_valores_y_se_lee()
    {
        var pedido = (await Db.Schemas.GetTableAsync(DatabaseFixture.ConnectionId, Schema, "pedido", default)).Table;
        var estado = pedido.FindColumn("estado")!;
        Assert.Equal(LogicalType.Enum, estado.Type);
        Assert.Equal(["borrador", "confirmado", "despachado", "anulado"], estado.EnumValues);

        var row = await Db.Crud.GetAsync(DatabaseFixture.ConnectionId, Schema, "pedido",
            new Dictionary<string, string> { ["id"] = "1" }, default);
        Assert.Equal("confirmado", row["estado"]);
    }

    [Fact]
    public async Task Json_se_detecta_se_guarda_y_se_lee()
    {
        var producto = (await Db.Schemas.GetTableAsync(DatabaseFixture.ConnectionId, Schema, "producto", default)).Table;
        Assert.Equal(LogicalType.Json, producto.FindColumn("atributos")!.Type); // en MariaDB es LONGTEXT + CHECK

        var created = await Db.Crud.CreateAsync(DatabaseFixture.ConnectionId, Schema, "producto", Body(
            ("codigo", UniqueCode()), ("nombre", "Con JSON"), ("categoria_id", 1), ("precio", 1),
            ("atributos", new { vegano = true })), default);
        Assert.Contains("vegano", Convert.ToString(created["atributos"]));
    }

    [Fact]
    public async Task Timestamp_se_devuelve_en_utc()
    {
        var row = await Db.Crud.GetAsync(DatabaseFixture.ConnectionId, Schema, "producto",
            new Dictionary<string, string> { ["id"] = "1" }, default);
        var value = Assert.IsType<DateTime>(row["creado_en"]);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public async Task Informa_sql_mode_e_intercalacion()
    {
        var def = new Core.Connections.ConnectionDefinition
        {
            Id = "prueba", Name = "Prueba", Engine = Db.Engine, ConnectionString = Db.ConnectionString
        };
        var result = await Db.Schemas.TestAsync(def, default);
        Assert.Contains("sql_mode", result.Server.Settings.Keys);
        Assert.Contains("collation_database", result.Server.Settings.Keys);
        Assert.Equal("+00:00", result.Server.Settings["time_zone"]);
    }
}

public sealed class MySqlTests(MySqlFixture db) : MySqlFamilyTests<MySqlFixture>(db)
{
    [Fact]
    public async Task Se_identifica_como_mysql() =>
        Assert.Equal("MySQL", (await Db.Schemas.TestAsync(new Core.Connections.ConnectionDefinition
        {
            Id = "p", Name = "p", Engine = Db.Engine, ConnectionString = Db.ConnectionString
        }, default)).Server.Product);
}

public sealed class MariaDbTests(MariaDbFixture db) : MySqlFamilyTests<MariaDbFixture>(db)
{
    [Fact]
    public async Task Se_identifica_como_mariadb() =>
        Assert.Equal("MariaDB", (await Db.Schemas.TestAsync(new Core.Connections.ConnectionDefinition
        {
            Id = "p", Name = "p", Engine = Db.Engine, ConnectionString = Db.ConnectionString
        }, default)).Server.Product);
}
