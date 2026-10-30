using System.ComponentModel;
using System.Data.Common;
using System.Threading.RateLimiting;
using Mantenedores.Api.Endpoints;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Mantenedores.Api.Mcp;

/// <summary>Límite de muestras pedidas por MCP: el mismo tope que las exportaciones (RateLimit:HeavyPerMinute).</summary>
public sealed class McpSampleLimiter(PartitionedRateLimiter<HttpContext> limiter)
{
    public PartitionedRateLimiter<HttpContext> Limiter { get; } = limiter;
}

/// <summary>
/// Herramientas del servidor MCP (P-114, D-101) para que una IA revise el esquema y proponga ajustes.
/// Cada llamada corre como el usuario de la solicitud HTTP (sesión o token <c>mnt_</c>, que actúa como su creador, D-100),
/// con sus mismos permisos. Nada se guarda directamente: un ajuste se deja como propuesta que una persona acepta en Configurar.
/// </summary>
[McpServerToolType]
public sealed class McpTools(IHttpContextAccessor accessor, SchemaService schemas, CrudService crud, IConnectionStore connections,
    IProposalStore proposals, Auth.HttpAccessContext access, McpSampleLimiter limiter)
{
    public sealed record ConnectionItem(string Id, string Name, string Engine, IReadOnlyList<string> Schemas, bool AllowSamples);

    public sealed record TableDetail(TableInfo Catalog, TableOverride? CurrentSettings, IReadOnlyList<string> Warnings);

    private HttpContext Http => accessor.HttpContext ?? throw new McpException("Sin solicitud HTTP.");

    [McpServerTool(Name = "list_connections", Title = "Conexiones", ReadOnly = true, Idempotent = true),
     Description("Lista las conexiones a bases de datos que el usuario puede ver: id, nombre, motor y si permite ver una muestra de datos.")]
    public Task<IReadOnlyList<ConnectionItem>> ListConnections(CancellationToken ct) => Run(async () =>
    {
        var visible = await ConnectionEndpoints.VisibleAsync(Http, access, ct);
        return (IReadOnlyList<ConnectionItem>)(await connections.ListAsync(ct)).Where(c => visible(c.Id))
            .Select(c => new ConnectionItem(c.Id, c.Name, c.Engine.Name, c.Schemas, c.AllowSamples)).ToList();
    });

    [McpServerTool(Name = "list_tables", Title = "Tablas", ReadOnly = true, Idempotent = true),
     Description("Lista las tablas y vistas de una conexión con lo básico (columnas, PK, FK, nombre visible, grupo) y los avisos de ajustes que no se pudieron aplicar.")]
    public Task<MetaEndpoints.SchemaSummary> ListTables([Description("Id de la conexión (de list_connections).")] string connection,
        CancellationToken ct) => Run(async () => MetaEndpoints.Summarize((await schemas.GetContextAsync(connection, ct)).Schema));

    [McpServerTool(Name = "get_table", Title = "Tabla", ReadOnly = true, Idempotent = true),
     Description("Definición de una tabla tal como la lee el catálogo de la base (columnas, tipos, PK, FK, índices) y su ajuste actual (currentSettings, null si no tiene). Úsela antes de proponer un ajuste.")]
    public Task<TableDetail> GetTable(
        [Description("Id de la conexión.")] string connection,
        [Description("Esquema de la tabla (en MySQL/MariaDB, la base).")] string schema,
        [Description("Nombre de la tabla.")] string table, CancellationToken ct) => Run(async () =>
    {
        var info = await MetaEndpoints.RawTableAsync(schemas, connection, schema, table, ct);
        var current = await MetaEndpoints.FindAsync(schemas, connection, info, ct);
        var warnings = (await schemas.GetUnrestrictedContextAsync(connection, ct)).Schema.OverrideWarnings
            .Where(w => w.Contains($"{info.Schema}.{info.Name}", StringComparison.OrdinalIgnoreCase)).ToList();
        return new TableDetail(info, current, warnings);
    });

    [McpServerTool(Name = "suggest_settings", Title = "Sugerencias sin IA", ReadOnly = true, Idempotent = true),
     Description("Sugerencias que el servicio calcula solo con reglas sobre el catálogo (relaciones virtuales, filtros de combos, columnas técnicas, correos). Son un buen punto de partida: revíselas antes de proponer otras.")]
    public Task<IReadOnlyList<Suggestion>> SuggestSettings(
        [Description("Id de la conexión.")] string connection, [Description("Esquema.")] string schema,
        [Description("Tabla.")] string table, CancellationToken ct) =>
        Run(() => MetaEndpoints.SuggestionsAsync(schemas, connection, schema, table, ct));

    [McpServerTool(Name = "sample_rows", Title = "Muestra de datos", ReadOnly = true),
     Description("Las filas más recientes de una tabla (hasta 1000), con los permisos del usuario, para entender qué contienen las columnas. Solo si la conexión lo permite (allowSamples). orderedBy dice qué columnas se usaron para elegir las más recientes.")]
    public Task<SampleResult> SampleRows(
        [Description("Id de la conexión.")] string connection, [Description("Esquema.")] string schema,
        [Description("Tabla.")] string table,
        [Description("Cuántas filas (1 a 1000; por defecto 100).")] int take = 100, CancellationToken ct = default) => Run(async () =>
    {
        using var lease = await limiter.Limiter.AcquireAsync(Http, 1, ct);
        if (!lease.IsAcquired) throw new McpException("Demasiadas muestras seguidas: espere un minuto y vuelva a intentar.");
        return await crud.SampleAsync(connection, schema, table, Math.Clamp(take, 1, CrudService.MaxSampleRows), ct);
    });

    [McpServerTool(Name = "preview_settings", Title = "Probar un ajuste", ReadOnly = true, Idempotent = true),
     Description("Aplica un ajuste a la tabla sin guardarlo y devuelve cómo quedaría y sus errores por campo (vacío = válido). El ajuste reemplaza al actual entero: parta de currentSettings de get_table.")]
    public Task<MetaEndpoints.OverridePreview> PreviewSettings(
        [Description("Id de la conexión.")] string connection, [Description("Esquema.")] string schema,
        [Description("Tabla.")] string table,
        [Description("Ajuste completo de la tabla (mismo formato que currentSettings).")] TableOverride settings, CancellationToken ct) =>
        Run(() => MetaEndpoints.PreviewAsync(schemas, connection, schema, table, settings, ct));

    [McpServerTool(Name = "propose_settings", Title = "Proponer un ajuste", Destructive = false),
     Description("Deja un ajuste como propuesta pendiente con su motivo. No cambia nada: una persona la revisa y la acepta o descarta en Configurar. Debe ser válido (pruébelo antes con preview_settings) y reemplaza al ajuste actual entero.")]
    public Task<OverrideProposal> ProposeSettings(
        [Description("Id de la conexión.")] string connection, [Description("Esquema.")] string schema,
        [Description("Tabla.")] string table,
        [Description("Ajuste completo de la tabla (mismo formato que currentSettings).")] TableOverride settings,
        [Description("Por qué se propone, en pocas líneas: lo leerá quien decida.")] string reason, CancellationToken ct) =>
        Run(() => MetaEndpoints.ProposeAsync(schemas, proposals, connection, schema, table, settings, reason, Http.User.Identity?.Name, ct));

    [McpServerTool(Name = "list_proposals", Title = "Propuestas", ReadOnly = true, Idempotent = true),
     Description("Propuestas de ajustes de una conexión, para no repetir una que ya está pendiente o fue descartada.")]
    public Task<IReadOnlyList<OverrideProposal>> ListProposals(
        [Description("Id de la conexión.")] string connection,
        [Description("pending (por defecto), accepted o discarded.")] string status = ProposalStatus.Pending, CancellationToken ct = default) =>
        Run(async () =>
        {
            await schemas.GetDefinitionAsync(connection, ct);
            return await proposals.ListAsync(connection, status, ct);
        });

    /// <summary>Los errores del servicio llegan a la IA con su mensaje y sus campos, para que corrija y reintente.</summary>
    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (ValidationException v)
        {
            throw new McpException($"{v.Message} {string.Join("; ", v.Errors.Select(e => $"{e.Key}: {e.Value}"))}");
        }
        catch (MantenedorException m) { throw new McpException(m.Message); }
        // Como en la API (ErrorHandler): el usuario necesita saber qué dijo la base.
        catch (DbException db) { throw new McpException($"La base de datos respondió: {db.Message}"); }
    }
}
