using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Mantenedores.Api.Auth;
using Mantenedores.Api.Infrastructure;
using ModelContextProtocol.Protocol;

namespace Mantenedores.Api.Mcp;

/// <summary>Servidor MCP (P-114, D-101), sección <c>Mcp</c>. Apagado por defecto.</summary>
public sealed class McpOptions
{
    public const string Section = "Mcp";

    /// <summary>Publica <c>/api/mcp</c> para clientes MCP (Claude, Cursor, etc.) con un token de acceso o un JWT.</summary>
    public bool Enabled { get; set; }
}

public static class McpSetup
{
    public const string Path = "/api/mcp";

    private const string Instructions =
        "Servicio de mantenedores: genera pantallas CRUD a partir del esquema de una base de datos. Usted ayuda a configurarlas. " +
        "Flujo sugerido: list_connections → list_tables → get_table → suggest_settings (reglas sin IA) → sample_rows si la conexión lo " +
        "permite → preview_settings hasta que no haya errores → propose_settings con un motivo claro. No puede guardar ajustes: " +
        "sus propuestas quedan pendientes hasta que una persona las acepte en Configurar. Revise list_proposals para no repetirlas. " +
        "Un ajuste reemplaza al actual de la tabla entero: parta siempre de currentSettings.";

    public static void AddMantenedoresMcp(this IServiceCollection services, McpOptions o, RateLimitOptions limits)
    {
        if (!o.Enabled) return;
        services.AddHttpContextAccessor();
        services.AddSingleton(new McpSampleLimiter(RateLimiting.HeavyLimiter(limits)));
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            // Sin \u0022 en cada comilla: el texto llega a la IA tal cual y gasta menos tokens.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        services.AddMcpServer(s =>
            {
                s.ServerInfo = new Implementation { Name = "mantenedores", Version = typeof(McpSetup).Assembly.GetName().Version?.ToString() ?? "1.0" };
                s.ServerInstructions = Instructions;
            })
            // Sin estado: cada llamada es una solicitud HTTP con su usuario, así los permisos se revisan cada vez.
            .WithHttpTransport(t => t.Stateless = true)
            .WithTools<McpTools>(json);
    }

    /// <summary>Solo quien puede configurar tablas (ConfigureTables): las herramientas leen y proponen ajustes.</summary>
    public static void MapMantenedoresMcp(this WebApplication app, McpOptions o)
    {
        if (o.Enabled) app.MapMcp(Path).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));
    }
}
