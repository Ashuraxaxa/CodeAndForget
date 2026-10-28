using System.Data.Common;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.IntegrationTests.Infrastructure;

public class PostgresFixture : DatabaseFixture
{
    private const string Password = "Demo_Pass_123";

    public override DatabaseEngine Engine => DatabaseEngine.Postgres;
    public override IDatabaseProvider Provider { get; } = new PostgresProvider();

    // Versión mínima soportada (D-035). La CI también prueba la actual con MANTENEDORES_PG_IMAGE.
    protected override string DefaultImage => "postgres:12";
    protected override string ImageVariable => "MANTENEDORES_PG_IMAGE";
    protected override int InternalPort => 5432;

    protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["POSTGRES_PASSWORD"] = Password,
        ["POSTGRES_DB"] = "demo"
    };

    public override int MinimumMajorVersion => 12;
    public override IReadOnlyList<string> ReadOnlyUserSql(string user, string password) =>
        [$"CREATE ROLE {user} LOGIN PASSWORD '{password}'", $"GRANT USAGE ON SCHEMA ventas TO {user}",
         $"GRANT SELECT ON ALL TABLES IN SCHEMA ventas TO {user}"];

    public override string AnalyzeSql(string quotedTable) => "ANALYZE " + quotedTable;

    public override string CreateActionRoutineSql => """
        CREATE FUNCTION ventas.subir_precio(p_id int, p_monto numeric) RETURNS TABLE (precio numeric) LANGUAGE sql AS
        $$ UPDATE ventas.producto SET precio = producto.precio + p_monto WHERE id = p_id RETURNING producto.precio $$
        """;
    public override string DropActionRoutineSql => "DROP FUNCTION ventas.subir_precio(int, numeric)";

    protected override string MajorVersionSql => "SELECT current_setting('server_version_num')::int / 10000";

    protected override string BuildConnectionString(string host, int port, bool forSeed) =>
        $"Host={host};Port={port};Database=demo;Username=postgres;Password={Password};Include Error Detail=true";

    protected override async Task SeedAsync(DbConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = await File.ReadAllTextAsync(SamplePath("postgres"));
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// PostgreSQL con PostGIS (P-104): la base de ejemplo más columnas geography y geometry. Solo para las pruebas espaciales;
/// la batería común corre en <see cref="PostgresFixture"/>.
/// </summary>
public sealed class PostgisFixture : PostgresFixture
{
    protected override string DefaultImage => "postgis/postgis:12-3.4";
    protected override string ImageVariable => "MANTENEDORES_POSTGIS_IMAGE";

    protected override async Task SeedAsync(DbConnection connection)
    {
        await base.SeedAsync(connection);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS postgis;
            ALTER TABLE ventas.cliente ADD COLUMN ubicacion geography(Point, 4326), ADD COLUMN zona geometry, ADD COLUMN plano geometry(Point, 32719);
            UPDATE ventas.cliente SET ubicacion = ST_GeogFromText('POINT(-70.66 -33.45)') WHERE rut = '76.123.456-7';
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}

