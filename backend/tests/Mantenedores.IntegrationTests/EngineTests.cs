using Mantenedores.Core.Query;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Schema;
using Mantenedores.IntegrationTests.Infrastructure;

namespace Mantenedores.IntegrationTests;

/// <summary>PostgreSQL: batería común + casos propios del motor (enum, jsonb).</summary>
public sealed class PostgresTests(PostgresFixture db) : ProviderContractTests<PostgresFixture>(db)
{
    [Fact]
    public async Task Arreglo_se_lee_como_lista_y_se_guarda_validando_cada_elemento()
    {
        var cliente = (await Db.Schemas.GetTableAsync("demo", Schema, "cliente", default)).Table;
        var col = cliente.FindColumn("telefonos")!;
        Assert.Equal((LogicalType.Array, (LogicalType?)LogicalType.Text, "text"), (col.Type, col.ElementType, col.ElementNativeType));

        var list = await Db.Crud.ListAsync("demo", Schema, "cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["rut"] = "76.123.456-7" }
        }, default);
        var row = Assert.Single(list.Rows);
        Assert.Equal(["+56 2 2345 6789", "+56 9 8765 4321"], (string[])row["telefonos"]!);

        var key = Key(("id", row["id"]!));
        try
        {
            var updated = await Db.Crud.UpdateAsync("demo", Schema, "cliente", key,
                Body(("telefonos", new[] { "111", "222", "333" })), default);
            Assert.Equal(["111", "222", "333"], (string[])updated["telefonos"]!);
        }
        finally
        {
            await Db.Crud.UpdateAsync("demo", Schema, "cliente", key,
                Body(("telefonos", new[] { "+56 2 2345 6789", "+56 9 8765 4321" })), default);
        }

        // Un arreglo de enteros valida cada elemento antes de ir a la base.
        await Db.ScalarAsync("CREATE TABLE ventas.arreglo_prueba (id int PRIMARY KEY, nums int4[])");
        try
        {
            await Db.Schemas.RefreshAsync("demo", default);
            await Db.Crud.CreateAsync("demo", Schema, "arreglo_prueba", Body(("id", 1), ("nums", new object?[] { 1, "2", null })), default);
            var nums = (await Db.Crud.GetAsync("demo", Schema, "arreglo_prueba", Key(("id", 1)), default))["nums"];
            Assert.Equal(new string?[] { "1", "2", null }, (string?[])nums!); // se leen como texto, con sus nulos
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                Db.Crud.CreateAsync("demo", Schema, "arreglo_prueba", Body(("id", 2), ("nums", new[] { "1", "x" })), default));
            Assert.StartsWith("elemento 2:", ex.Errors["nums"]);
        }
        finally
        {
            await Db.ScalarAsync("DROP TABLE ventas.arreglo_prueba");
            await Db.Schemas.RefreshAsync("demo", default);
        }
    }

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
    public async Task Geografia_y_jerarquia_se_leen_como_texto_de_solo_lectura()
    {
        var cliente = (await Db.Schemas.GetTableAsync("demo", Schema, "Cliente", default)).Table;
        Assert.Equal(LogicalType.ReadOnlyText, cliente.FindColumn("Ubicacion")!.Type);
        Assert.True(cliente.FindColumn("Ubicacion")!.IsReadOnly);

        var list = await Db.Crud.ListAsync("demo", Schema, "Cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["Rut"] = "76.123.456-7" }
        }, default);
        Assert.Equal("POINT (-70.66 -33.45)", Assert.Single(list.Rows)["Ubicacion"]);

        var empleados = await Db.Crud.ListAsync("demo", Schema, "Empleado", new ListRequest { Sort = [new SortSpec("Id")] }, default);
        Assert.Equal("/", empleados.Rows[0]["Nodo"]);
        Assert.Equal("/1/", empleados.Rows[1]["Nodo"]);

        // Escribirla se ignora (es de solo lectura), sin error: el resto del cambio se guarda.
        var key = Key(("Id", empleados.Rows[1]["Id"]!));
        var nombre = (string)empleados.Rows[1]["Nombre"]!;
        var updated = await Db.Crud.UpdateAsync("demo", Schema, "Empleado", key, Body(("Nodo", "/9/"), ("Nombre", nombre)), default);
        Assert.Equal("/1/", updated["Nodo"]);
    }

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
    public async Task Punto_geografico_se_lee_como_wkt()
    {
        var cliente = (await Db.Schemas.GetTableAsync("demo", Schema, "cliente", default)).Table;
        Assert.Equal(LogicalType.ReadOnlyText, cliente.FindColumn("ubicacion")!.Type);
        var list = await Db.Crud.ListAsync("demo", Schema, "cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["rut"] = "76.123.456-7" }
        }, default);
        Assert.Equal("POINT(-70.66 -33.45)", Assert.Single(list.Rows)["ubicacion"]);
    }

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
