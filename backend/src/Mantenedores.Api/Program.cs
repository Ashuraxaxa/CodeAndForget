using System.Text.Json.Serialization;
using Mantenedores.Api.Auth;
using Mantenedores.Api.Endpoints;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Api.Notifications;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Notifications;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Security;
using Mantenedores.Core.Services;
using Mantenedores.Metadata;
using Mantenedores.Providers.MySql;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;

// Utilidad para generar el hash de una contraseña: dotnet run -- hash-password "mi-clave"
if (args is ["hash-password", var plain])
{
    Console.WriteLine(PasswordHasher.Hash(plain));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Corre igual como proceso, contenedor, servicio de Windows o servicio systemd (D-022).
// Ambos son inactivos si la app no se ejecuta bajo ese administrador de servicios.
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

// Una ruta relativa se resuelve contra la carpeta de la app, no contra el directorio de trabajo
// (como servicio de Windows el directorio de trabajo es C:\Windows\System32).
var dataDir = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? "data", builder.Environment.ContentRootPath);

// --- Publicación: subruta y proxy (P-007) ---
var hosting = builder.Configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
builder.Services.ConfigureForwardedHeaders(hosting);

// --- Autenticación: modo Local o cualquier emisor JWT/OIDC (D-024) ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
var auth = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddMantenedoresAuth(auth);

// --- Metadata del servicio (conexiones) y llaves de cifrado: SQLite por defecto, o Postgres / SQL Server (D-023, D-025) ---
builder.Services.AddMetadataStore(builder.Configuration, dataDir);

// --- Motores soportados: agregar uno nuevo = registrar su IDatabaseProvider ---
builder.Services.AddSingleton<IDatabaseProvider, SqlServerProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgresProvider>();
builder.Services.AddSingleton<IDatabaseProvider, MySqlProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

builder.Services.AddSingleton(builder.Configuration.GetSection("Crud").Get<CrudOptions>() ?? new CrudOptions());
builder.Services.AddSingleton(new SecretResolver(builder.Configuration.GetSection(SecretOptions.Section).Get<SecretOptions>() ?? new SecretOptions()));
builder.Services.AddSingleton<SecurityService>();
builder.Services.AddSingleton(builder.Configuration.GetSection(PoolOptions.Section).Get<PoolOptions>() ?? new PoolOptions());
builder.Services.AddSingleton<SchemaService>();
// --- Auditoría de cambios (D-065): activa por defecto, 365 días ---
var auditOptions = builder.Configuration.GetSection(AuditOptions.Section).Get<AuditOptions>() ?? new AuditOptions();
builder.Services.AddSingleton(auditOptions);
builder.Services.AddSingleton(sp =>
{
    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Mantenedores.Auditoria");
    return new AuditService(sp.GetRequiredService<IAuditStore>(), auditOptions,
        ex => log.LogError(ex, "No se pudo registrar un cambio en la auditoría (el cambio sí se guardó)"));
});
builder.Services.AddHostedService<AuditPurgeService>();
// --- Avisos por correo de tablas críticas (D-073): apagados por defecto ---
var notifications = builder.Configuration.GetSection(NotificationOptions.Section).Get<NotificationOptions>() ?? new NotificationOptions();
builder.Services.AddSingleton(notifications);
builder.Services.AddSingleton<SmtpChangeNotifier>();
builder.Services.AddSingleton<IChangeNotifier>(sp => sp.GetRequiredService<SmtpChangeNotifier>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SmtpChangeNotifier>());
builder.Services.AddSingleton<CrudService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// --- Límites de uso por usuario, para exportar/importar y para el login (P-051, P-091) ---
builder.Services.AddMantenedoresRateLimiting(
    builder.Configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions());
// --- Observabilidad (P-053): OpenTelemetry por OTLP si se configura un destino; log de solicitudes lentas siempre ---
builder.Services.AddMantenedoresTelemetry(
    builder.Configuration.GetSection(TelemetryOptions.Section).Get<TelemetryOptions>() ?? new TelemetryOptions());
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddMantenedoresHealthChecks();

var app = builder.Build();

// Migraciones de la metadata e importación del connections.json de la Fase 1, antes de atender solicitudes.
await app.Services.InitializeMetadataAsync(dataDir);

app.UseExceptionHandler();
app.UseSlowRequestLog();
app.UseHosting(hosting);
app.UseStaticFiles(); // Recursos del frontend compilado (wwwroot). index.html lo sirve MapSpaIndex.
app.UseAuthentication();
app.UseRateLimiter(); // después de autenticar: el límite es por usuario
app.UseAuthorization();

app.MapMantenedoresHealthChecks();
app.MapAuth();
app.MapConnections();
app.MapMeta();
app.MapData();
app.MapSecurity();
app.MapAudit();
// Una ruta /api desconocida es 404; cualquier otra ruta la resuelve el router del frontend.
app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapSpaIndex();

await app.RunAsync();

public partial class Program;
