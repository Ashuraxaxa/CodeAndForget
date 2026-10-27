using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Mantenedores.Metadata;

/// <summary>
/// Base de metadata del servicio. Las diferencias entre motores se limitan a crear la conexión y a la tabla
/// de migraciones: los scripts de cada motor viven en Migrations/&lt;Motor&gt;/ y el resto del SQL es portable.
/// </summary>
public sealed class MetadataDatabase
{
    public MetadataProvider Provider { get; }
    public string ConnectionString { get; }

    public MetadataDatabase(MetadataOptions options, string dataDirectory)
    {
        Provider = options.Provider;
        ConnectionString = options.Provider switch
        {
            MetadataProvider.Sqlite when options.ConnectionString.Length == 0 =>
                new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "mantenedores.db") }.ToString(),
            MetadataProvider.Postgres or MetadataProvider.SqlServer when options.ConnectionString.Length == 0 =>
                throw new InvalidOperationException(
                    $"Metadata:Provider={options.Provider}: configure Metadata:ConnectionString (variable de entorno Metadata__ConnectionString)."),
            _ => options.ConnectionString
        };

        if (Provider == MetadataProvider.Sqlite)
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(
                new SqliteConnectionStringBuilder(ConnectionString).DataSource))!);
    }

    public DbConnection CreateConnection() => Provider switch
    {
        MetadataProvider.Sqlite => new SqliteConnection(ConnectionString),
        MetadataProvider.Postgres => new NpgsqlConnection(ConnectionString),
        MetadataProvider.SqlServer => new SqlConnection(ConnectionString),
        _ => throw new NotSupportedException($"Metadata:Provider={Provider} no usa base de datos.")
    };

    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return conn;
    }

    /// <summary>Carpeta de scripts de este motor.</summary>
    internal string MigrationsFolder => Provider.ToString();

    internal string CreateMigrationTableSql => Provider == MetadataProvider.SqlServer
        ? "IF OBJECT_ID(N'mnt_migration', N'U') IS NULL CREATE TABLE mnt_migration (id nvarchar(100) NOT NULL PRIMARY KEY, applied_at datetimeoffset NOT NULL)"
        : Provider == MetadataProvider.Postgres
            ? "CREATE TABLE IF NOT EXISTS mnt_migration (id varchar(100) NOT NULL PRIMARY KEY, applied_at timestamptz NOT NULL)"
            : "CREATE TABLE IF NOT EXISTS mnt_migration (id VARCHAR(100) NOT NULL PRIMARY KEY, applied_at VARCHAR(40) NOT NULL)";
}
