using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.SqlServer;

namespace Mantenedores.IntegrationTests.Infrastructure;

public sealed partial class SqlServerFixture : DatabaseFixture
{
    private const string Password = "Demo_Pass_123!";

    public override DatabaseEngine Engine => DatabaseEngine.SqlServer;
    public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    // SQL Server 2016 no tiene imagen Linux: 2017 es la más antigua disponible en contenedor.
    // La compatibilidad con 2016 se cuida no usando T-SQL posterior (ver docs/motores.md).
    protected override string DefaultImage => "mcr.microsoft.com/mssql/server:2017-latest";
    protected override string ImageVariable => "MANTENEDORES_MSSQL_IMAGE";
    protected override int InternalPort => 1433;

    protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["ACCEPT_EULA"] = "Y",
        ["MSSQL_SA_PASSWORD"] = Password
    };

    // 13 = SQL Server 2016, 14 = 2017, 15 = 2019, 16 = 2022.
    public override int MinimumMajorVersion => 13;
    public override string CreateActionRoutineSql => """
        CREATE PROCEDURE ventas.subir_precio @id int, @monto decimal(12,2) AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE ventas.Producto SET Precio = Precio + @monto WHERE Id = @id;
            SELECT Precio AS precio FROM ventas.Producto WHERE Id = @id;
        END
        """;
    public override string DropActionRoutineSql => "DROP PROCEDURE ventas.subir_precio";

    public override IReadOnlyList<string> ReadOnlyUserSql(string user, string password) =>
        [$"CREATE LOGIN {user} WITH PASSWORD = '{password}', CHECK_POLICY = OFF", $"CREATE USER {user} FOR LOGIN {user}",
         $"GRANT SELECT ON SCHEMA::ventas TO {user}"];

    public override string AnalyzeSql(string quotedTable) => "UPDATE STATISTICS " + quotedTable;

    protected override string MajorVersionSql => "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)";

    protected override string BuildConnectionString(string host, int port, bool forSeed) =>
        $"Server={host},{port};Database={(forSeed ? "master" : "Demo")};User Id=sa;Password={Password};TrustServerCertificate=True";

    /// <summary>La base de SQL Server usa PascalCase: categoria_id → CategoriaId, v_productos → VProductos.</summary>
    public override string Name(string logical) =>
        string.Concat(logical.Split('_').Select(p => p.Length == 0 ? p : char.ToUpper(p[0], CultureInfo.InvariantCulture) + p[1..]));

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();

    protected override async Task SeedAsync(DbConnection connection)
    {
        // El script usa separadores GO (de sqlcmd): se ejecuta lote por lote en la misma conexión.
        var script = await File.ReadAllTextAsync(SamplePath("sqlserver"));
        foreach (var batch in GoSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
