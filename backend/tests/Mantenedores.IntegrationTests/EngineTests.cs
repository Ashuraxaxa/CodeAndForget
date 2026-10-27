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
