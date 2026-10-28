using System.Data.Common;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : DatabaseFixture
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
    public override string AnalyzeSql(string quotedTable) => "ANALYZE " + quotedTable;

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
