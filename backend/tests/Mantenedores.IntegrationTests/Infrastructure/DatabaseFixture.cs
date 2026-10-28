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

    /// <summary>Usuario de las pruebas: sin restricción salvo que una prueba lo cambie (y lo devuelva).</summary>
    public SwitchableAccess Access { get; } = new();

    /// <summary>Auditoría en memoria: las pruebas leen lo que registró el CRUD.</summary>
    public InMemoryAuditStore Audit { get; } = new();

    /// <summary>Avisos por correo "enviados" (D-073): las pruebas leen lo que encoló el CRUD.</summary>
    public RecordingNotifier Notices { get; } = new();
    public CrudService Crud { get; private set; } = null!;

    /// <summary>Ajustes en memoria (P-011); cada prueba que los usa los borra al terminar.</summary>
    public InMemoryOverrideStore Overrides { get; } = new();

    /// <summary>Foto de referencia del esquema en memoria (P-013).</summary>
    public InMemorySnapshotStore Snapshots { get; } = new();

    /// <summary>Cadena de conexión para el host y puerto publicados del contenedor.</summary>
    protected abstract string BuildConnectionString(string host, int port, bool forSeed);

    /// <summary>Carga <c>samples/&lt;motor&gt;/01_demo.sql</c>.</summary>
    protected abstract Task SeedAsync(DbConnection connection);

    /// <summary>Consulta que devuelve la versión principal del servidor como entero.</summary>
    protected abstract string MajorVersionSql { get; }

    /// <summary>Traduce un nombre lógico (snake_case, como en la base de Postgres) al nombre real del motor.</summary>
    public virtual string Name(string logical) => logical;

    /// <summary>Actualiza las estadísticas de una tabla (ya delimitada), para probar el total estimado (D-072).</summary>
    public abstract string AnalyzeSql(string quotedTable);

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
        Schemas = new SchemaService(store, new ProviderRegistry([Provider]), Overrides, Snapshots, access: Access);
        Crud = new CrudService(Schemas, new CrudOptions { CommandTimeoutSeconds = 60 },
            new Mantenedores.Core.Audit.AuditService(Audit, new Mantenedores.Core.Audit.AuditOptions()), Notices);
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

    /// <summary>Ejecuta una sentencia directa, sin leer su resultado (p. ej. ANALYZE TABLE, que en MySQL devuelve filas).</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var db = Provider.CreateConnection(ConnectionString);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.NextResultAsync()) { }
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

/// <summary>Acceso que una prueba puede cambiar para simular un usuario con perfiles (D-063).</summary>
public sealed class SwitchableAccess : Mantenedores.Core.Security.IAccessContext
{
    public Mantenedores.Core.Security.UserAccess Current { get; set; } = Mantenedores.Core.Security.UserAccess.All;

    public ValueTask<Mantenedores.Core.Security.UserAccess> CurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current);
}

public sealed class InMemoryAuditStore : Mantenedores.Core.Audit.IAuditStore
{
    private readonly List<Mantenedores.Core.Audit.AuditEntry> _entries = [];

    public IReadOnlyList<Mantenedores.Core.Audit.AuditEntry> For(string schema, string table, string? rowKey = null)
    {
        lock (_entries)
            return _entries.Where(e => string.Equals(e.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase) &&
                                       (rowKey is null || e.RowKey == rowKey)).ToList();
    }

    public Task WriteAsync(IReadOnlyList<Mantenedores.Core.Audit.AuditEntry> entries, CancellationToken ct)
    {
        lock (_entries) _entries.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<Mantenedores.Core.Audit.AuditPage> QueryAsync(Mantenedores.Core.Audit.AuditQuery query, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct) => Task.FromResult(0);
}

public sealed class RecordingNotifier : Mantenedores.Core.Notifications.IChangeNotifier
{
    private readonly List<Mantenedores.Core.Notifications.ChangeNotice> _notices = [];
    public bool Enabled => true;

    public IReadOnlyList<Mantenedores.Core.Notifications.ChangeNotice> For(string table)
    {
        lock (_notices) return _notices.Where(n => string.Equals(n.Table, table, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void Enqueue(Mantenedores.Core.Notifications.ChangeNotice notice)
    {
        lock (_notices) _notices.Add(notice);
    }
}
