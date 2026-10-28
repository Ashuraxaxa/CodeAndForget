using System.Data.Common;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.MySql;

namespace Mantenedores.IntegrationTests.Infrastructure;

/// <summary>Base para MySQL y MariaDB: mismo proveedor, misma base de ejemplo (samples/mysql).</summary>
public abstract class MySqlFamilyFixture : DatabaseFixture
{
    private const string Password = "Demo_Pass_123";

    public override DatabaseEngine Engine => DatabaseEngine.MySql;
    public override IDatabaseProvider Provider { get; } = new MySqlProvider();
    protected override int InternalPort => 3306;

    protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["MYSQL_ROOT_PASSWORD"] = Password,
        ["MARIADB_ROOT_PASSWORD"] = Password
    };

    public override IReadOnlyList<string> ReadOnlyUserSql(string user, string password) =>
        [$"CREATE USER '{user}'@'%' IDENTIFIED BY '{password}'", $"GRANT SELECT ON ventas.* TO '{user}'@'%'"];

    public override string AnalyzeSql(string quotedTable) => "ANALYZE TABLE " + quotedTable;

    protected override string MajorVersionSql => "SELECT CAST(SUBSTRING_INDEX(VERSION(), '.', 1) AS SIGNED)";

    // En MySQL el esquema es la base de datos: la cadena de conexión apunta a "ventas" (D-032).
    protected override string BuildConnectionString(string host, int port, bool forSeed) =>
        $"Server={host};Port={port};User ID=root;Password={Password};{(forSeed ? "" : "Database=ventas;")}";

    protected override async Task SeedAsync(DbConnection connection)
    {
        // MySqlConnector admite varias sentencias por comando: el script se ejecuta completo.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = await File.ReadAllTextAsync(SamplePath("mysql"));
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed class MySqlFixture : MySqlFamilyFixture
{
    // Versión mínima soportada (D-035). La CI también prueba la actual con MANTENEDORES_MYSQL_IMAGE.
    protected override string DefaultImage => "mysql:8.0";
    protected override string ImageVariable => "MANTENEDORES_MYSQL_IMAGE";
    public override int MinimumMajorVersion => 8;
}

public sealed class MariaDbFixture : MySqlFamilyFixture
{
    protected override string DefaultImage => "mariadb:10.6";
    protected override string ImageVariable => "MANTENEDORES_MARIADB_IMAGE";
    public override int MinimumMajorVersion => 10;
}
