using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Services;

namespace Mantenedores.IntegrationTests.Infrastructure;

/// <summary>
/// Levanta un motor real en un contenedor, carga la base de ejemplo de <c>samples/</c> y deja listos
/// los servicios de esquema y CRUD apuntando a ella. Cada motor hereda esta clase.
/// La imagen se puede cambiar con una variable de entorno (p. ej. para probar la versión mínima y la actual).
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    public const string ConnectionId = "demo";

    private IContainer? _container;

    public abstract DatabaseEngine Engine { get; }
    public abstract IDatabaseProvider Provider { get; }

    /// <summary>Imagen usada si no se define la variable de entorno.</summary>
    protected abstract string DefaultImage { get; }

    /// <summary>Variable de entorno que permite cambiar la imagen (p. ej. MANTENEDORES_PG_IMAGE).</summary>
    protected abstract string ImageVariable { get; }

    protected abstract int InternalPort { get; }
    protected abstract IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>Esquema donde está la base de ejemplo.</summary>
    public virtual string SampleSchema => "ventas";

    public string Image => System.Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } v ? v : DefaultImage;
    public string ConnectionString { get; private set; } = "";
    /// <summary>Versión principal del servidor (p. ej. 12 en PostgreSQL 12, 14 en SQL Server 2017).</summary>
    public int MajorVersion { get; private set; }

    /// <summary>Versión principal mínima soportada (D-035).</summary>
    public abstract int MinimumMajorVersion { get; }
    public SchemaService Schemas { get; private set; } = null!;
    public CrudService Crud { get; private set; } = null!;

    /// <summary>Cadena de conexión para el host y puerto publicados del contenedor.</summary>
    protected abstract string BuildConnectionString(string host, int port, bool forSeed);

    /// <summary>Carga <c>samples/&lt;motor&gt;/01_demo.sql</c>.</summary>
    protected abstract Task SeedAsync(DbConnection connection);

    /// <summary>Consulta que devuelve la versión principal del servidor como entero.</summary>
    protected abstract string MajorVersionSql { get; }

    /// <summary>Traduce un nombre lógico (snake_case, como en la base de Postgres) al nombre real del motor.</summary>
    public virtual string Name(string logical) => logical;

    public async Task InitializeAsync()
    {
        var builder = new ContainerBuilder(Image)
            .WithPortBinding(InternalPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(InternalPort));
        foreach (var (k, v) in Environment) builder = builder.WithEnvironment(k, v);

        _container = builder.Build();
        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(InternalPort);

        // El puerto abre antes de que el motor acepte conexiones: se reintenta hasta 3 minutos.
        await using (var seed = await ConnectWithRetryAsync(BuildConnectionString(host, port, forSeed: true)))
        {
            await SeedAsync(seed);
        }

        ConnectionString = BuildConnectionString(host, port, forSeed: false);
        await using (var db = Provider.CreateConnection(ConnectionString))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = MajorVersionSql;
            MajorVersion = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var store = new InMemoryConnectionStore(new ConnectionDefinition
        {
            Id = ConnectionId,
            Name = "Demo",
            Engine = Engine,
            ConnectionString = ConnectionString,
            Schemas = [SampleSchema]
        });
        Schemas = new SchemaService(store, new ProviderRegistry([Provider]));
        Crud = new CrudService(Schemas, new CrudOptions { CommandTimeoutSeconds = 60 });
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>Ejecuta una consulta escalar directa (para verificar efectos como triggers).</summary>
    public async Task<long> ScalarAsync(string sql)
    {
        await using var db = Provider.CreateConnection(ConnectionString);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    protected static string SamplePath(string engineFolder)
    {
        // Sube desde bin/ hasta encontrar la carpeta samples/ del repositorio.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("No se encontró la carpeta samples/ del repositorio.")
            : Path.Combine(dir.FullName, "samples", engineFolder, "01_demo.sql");
    }

    private async Task<DbConnection> ConnectWithRetryAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            var db = Provider.CreateConnection(connectionString);
            try
            {
                await db.OpenAsync();
                return db;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await db.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
