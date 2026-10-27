using System.Text.Json.Serialization;
using Mantenedores.Api.Auth;
using Mantenedores.Api.Endpoints;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Services;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

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

// --- Autenticación (Fase 1: usuarios en configuración + JWT) ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
var auth = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
if (auth.JwtKey.Length < 32)
    throw new InvalidOperationException("Configure Auth:JwtKey con al menos 32 caracteres (variable de entorno Auth__JwtKey).");

builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = auth.Issuer,
        IssuerSigningKey = TokenService.SigningKey(auth),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

// --- Conexiones: archivo JSON con secretos cifrados ---
builder.Services.AddDataProtection()
    .SetApplicationName("Mantenedores")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
builder.Services.AddSingleton<IConnectionStore>(sp =>
    new FileConnectionStore(Path.Combine(dataDir, "connections.json"), sp.GetRequiredService<IDataProtectionProvider>()));

// --- Motores soportados: agregar uno nuevo = registrar su IDatabaseProvider ---
builder.Services.AddSingleton<IDatabaseProvider, SqlServerProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgresProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

builder.Services.AddSingleton(builder.Configuration.GetSection("Crud").Get<CrudOptions>() ?? new CrudOptions());
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddSingleton<CrudService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddMantenedoresHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHosting(hosting);
app.UseStaticFiles(); // Recursos del frontend compilado (wwwroot). index.html lo sirve MapSpaIndex.
app.UseAuthentication();
app.UseAuthorization();

app.MapMantenedoresHealthChecks();
app.MapAuth();
app.MapConnections();
app.MapMeta();
app.MapData();
// Una ruta /api desconocida es 404; cualquier otra ruta la resuelve el router del frontend.
app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapSpaIndex();

app.Run();

public partial class Program;
