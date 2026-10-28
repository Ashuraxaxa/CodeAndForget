using System.Text.Json;
using Mantenedores.Core.Query;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Schema;
using Mantenedores.IntegrationTests.Infrastructure;

namespace Mantenedores.IntegrationTests;

/// <summary>PostgreSQL: batería común + casos propios del motor (enum, jsonb).</summary>
public sealed class PostgresTests(PostgresFixture db) : ProviderContractTests<PostgresFixture>(db)
{
    [Fact]
    public async Task Tipos_propios_se_leen_y_se_escriben_como_texto()
    {
        // P-075: compuesto, rango, red e intervalo, que antes quedaban sin soporte. El compuesto se crea después de
        // abrir conexiones: igual se lee, porque viaja como texto y no depende de la caché de tipos de Npgsql.
        await Db.ScalarAsync("CREATE TYPE ventas.direccion_prueba AS (calle text, numero int)");
        await Db.ScalarAsync("""
            CREATE TABLE ventas.tipos_prueba (id int PRIMARY KEY, dir ventas.direccion_prueba, vigencia daterange,
                                              ip inet, plazo interval)
            """);
        try
        {
            await Db.Schemas.RefreshAsync("demo", default);
            var info = (await Db.Schemas.GetTableAsync("demo", Schema, "tipos_prueba", default)).Table;
            Assert.All(new[] { "dir", "vigencia", "ip", "plazo" }, n => Assert.True(info.FindColumn(n)!.TextCast && !info.FindColumn(n)!.IsReadOnly, n));

            await Db.Crud.CreateAsync("demo", Schema, "tipos_prueba", Body(("id", 1), ("dir", "(\"Av. Siempre Viva\",742)"),
                ("vigencia", "[2026-01-01,2027-01-01)"), ("ip", "10.0.0.1"), ("plazo", "3 days")), default);
            var row = await Db.Crud.GetAsync("demo", Schema, "tipos_prueba", Key(("id", 1)), default);
            Assert.Equal("(\"Av. Siempre Viva\",742)", row["dir"]);
            Assert.Equal("[2026-01-01,2027-01-01)", row["vigencia"]);
            Assert.Equal("10.0.0.1", row["ip"]);
            Assert.Equal("3 days", row["plazo"]);

            // Filtro y búsqueda: como texto.
            var found = await Db.Crud.ListAsync("demo", Schema, "tipos_prueba", new ListRequest
            {
                Filters = new Dictionary<string, string> { ["ip"] = "10.0" }
            }, default);
            Assert.Single(found.Rows);
            await Assert.ThrowsAnyAsync<Exception>(() => Db.Crud.UpdateAsync("demo", Schema, "tipos_prueba", Key(("id", 1)), Body(("ip", "no-es-ip")), default));
        }
        finally
        {
            await Db.ScalarAsync("DROP TABLE ventas.tipos_prueba");
            await Db.ScalarAsync("DROP TYPE ventas.direccion_prueba");
            await Db.Schemas.RefreshAsync("demo", default);
        }
    }

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
    public async Task Geografia_se_edita_como_wkt_y_la_jerarquia_se_lee_como_texto()
    {
        var cliente = (await Db.Schemas.GetTableAsync("demo", Schema, "Cliente", default)).Table;
        Assert.Equal(LogicalType.Spatial, cliente.FindColumn("Ubicacion")!.Type);
        Assert.False(cliente.FindColumn("Ubicacion")!.IsReadOnly);

        var list = await Db.Crud.ListAsync("demo", Schema, "Cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["Rut"] = "76.123.456-7" }
        }, default);
        var row = Assert.Single(list.Rows);
        Assert.Equal("POINT (-70.66 -33.45)", row["Ubicacion"]);

        // P-090: se escribe como WKT (geography en SRID 4326) y se lee igual; un texto que no es WKT se rechaza claro.
        var ck = Key(("Id", row["Id"]!));
        var moved = await Db.Crud.UpdateAsync("demo", Schema, "Cliente", ck, Body(("Ubicacion", "POINT (-71.6 -33.04)")), default);
        Assert.Equal("POINT (-71.6 -33.04)", moved["Ubicacion"]);
        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            Db.Crud.UpdateAsync("demo", Schema, "Cliente", ck, Body(("Ubicacion", "-71.6, -33.04")), default));
        Assert.Contains("WKT", bad.Errors["Ubicacion"]);
        await Db.Crud.UpdateAsync("demo", Schema, "Cliente", ck, Body(("Ubicacion", "POINT (-70.66 -33.45)")), default);

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
    public async Task Punto_geografico_se_lee_y_se_edita_como_wkt()
    {
        var cliente = (await Db.Schemas.GetTableAsync("demo", Schema, "cliente", default)).Table;
        Assert.Equal(LogicalType.Spatial, cliente.FindColumn("ubicacion")!.Type);
        var list = await Db.Crud.ListAsync("demo", Schema, "cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["rut"] = "76.123.456-7" }
        }, default);
        var row = Assert.Single(list.Rows);
        Assert.Equal("POINT(-70.66 -33.45)", row["ubicacion"]);

        // P-090: se escribe como WKT y se lee igual; vacío lo deja nulo.
        var key = Key(("id", row["id"]!));
        Assert.Equal("POINT(-71.6 -33.04)", (await Db.Crud.UpdateAsync("demo", Schema, "cliente", key, Body(("ubicacion", "POINT (-71.6 -33.04)")), default))["ubicacion"]);
        Assert.Null((await Db.Crud.UpdateAsync("demo", Schema, "cliente", key, Body(("ubicacion", "")), default))["ubicacion"]);
        await Db.Crud.UpdateAsync("demo", Schema, "cliente", key, Body(("ubicacion", "POINT(-70.66 -33.45)")), default);
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
    public async Task Una_columna_con_srid_se_escribe_y_se_lee_en_longitud_latitud()
    {
        // P-104: MySQL 8 informa el SRID de la columna; en 4326 el WKT sigue en longitud latitud, como en los demás motores.
        await Db.ScalarAsync("CREATE TABLE srid_prueba (id INT PRIMARY KEY, punto POINT SRID 4326 NULL)");
        try
        {
            await Db.Schemas.RefreshAsync("demo", default);
            var info = (await Db.Schemas.GetTableAsync("demo", Schema, "srid_prueba", default)).Table;
            Assert.Equal((int?)4326, info.FindColumn("punto")!.Srid);
            await Db.Crud.CreateAsync("demo", Schema, "srid_prueba", Body(("id", 1), ("punto", "POINT(-70.66 -33.45)")), default);
            Assert.Equal("POINT(-70.66 -33.45)", (await Db.Crud.GetAsync("demo", Schema, "srid_prueba", Key(("id", 1)), default))["punto"]);
            // La latitud quedó como latitud: el orden se respetó.
            Assert.Equal(-3345L, Convert.ToInt64(await Db.ScalarAsync("SELECT ROUND(ST_Latitude(punto) * 100) FROM srid_prueba WHERE id = 1")));
        }
        finally
        {
            await Db.ScalarAsync("DROP TABLE srid_prueba");
            await Db.Schemas.RefreshAsync("demo", default);
        }
    }

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

/// <summary>PostGIS (P-104): geography y geometry editables como WKT, con el SRID de cada columna.</summary>
[Trait("Category", "Integration")]
public sealed class PostgisTests(PostgisFixture db) : IClassFixture<PostgisFixture>
{
    private static Dictionary<string, JsonElement> Body(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));

    [Fact]
    public async Task Geografia_y_geometria_se_editan_como_wkt_con_su_srid()
    {
        var cliente = (await db.Schemas.GetTableAsync("demo", "ventas", "cliente", default)).Table;
        Assert.Equal((LogicalType.Spatial, (int?)4326), (cliente.FindColumn("ubicacion")!.Type, cliente.FindColumn("ubicacion")!.Srid));
        Assert.Equal((int?)0, cliente.FindColumn("zona")!.Srid);
        Assert.Equal((int?)32719, cliente.FindColumn("plano")!.Srid);

        var row = Assert.Single((await db.Crud.ListAsync("demo", "ventas", "cliente", new ListRequest
        {
            Exact = new Dictionary<string, string> { ["rut"] = "76.123.456-7" }
        }, default)).Rows);
        Assert.Equal("POINT(-70.66 -33.45)", row["ubicacion"]);

        var key = new Dictionary<string, string> { ["id"] = Convert.ToString(row["id"], System.Globalization.CultureInfo.InvariantCulture)! };
        var updated = await db.Crud.UpdateAsync("demo", "ventas", "cliente", key, Body(
            ("ubicacion", "POINT (-71.6 -33.04)"), ("zona", "POLYGON((0 0, 1 0, 1 1, 0 0))"), ("plano", "POINT(350000 6300000)")), default);
        Assert.Equal("POINT(-71.6 -33.04)", updated["ubicacion"]);
        Assert.Equal("POLYGON((0 0,1 0,1 1,0 0))", updated["zona"]);
        Assert.Equal("POINT(350000 6300000)", updated["plano"]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            db.Crud.UpdateAsync("demo", "ventas", "cliente", key, Body(("ubicacion", "-71, -33")), default));
        Assert.Contains("WKT", ex.Errors["ubicacion"]);
    }
}
