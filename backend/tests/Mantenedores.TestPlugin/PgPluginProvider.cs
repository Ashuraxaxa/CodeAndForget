using System.Data.Common;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.Postgres;

namespace Mantenedores.TestPlugin;

/// <summary>Motor "PgPlugin": todo lo hace el proveedor de PostgreSQL, que viaja en la carpeta del plugin.</summary>
public sealed class PgPluginProvider : IDatabaseProvider
{
    public static readonly DatabaseEngine Name = new("PgPlugin");

    private readonly PostgresProvider _inner = new();

    public DatabaseEngine Engine => Name;
    public string DisplayName => "PostgreSQL (plugin)";
    public string? ExampleConnectionString => "Host=servidor;Database=base;Username=usuario;Password=***";
    public ISqlDialect Dialect => _inner.Dialect;
    public ISchemaProvider Schema => _inner.Schema;
    public DbConnection CreateConnection(string connectionString) => _inner.CreateConnection(connectionString);
    public DbErrorInfo ClassifyError(DbException exception) => _inner.ClassifyError(exception);
    public Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct) => _inner.GetServerInfoAsync(connection, ct);
    public string ApplyPool(string connectionString, PoolOptions pool) => _inner.ApplyPool(connectionString, pool);
}
