using System.Data.Common;
using System.Text.RegularExpressions;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

public static partial class ConnectionEndpoints
{
    public sealed record ConnectionRequest(string Id, string Name, DatabaseEngine Engine, string? ConnectionString, List<string>? Schemas, bool AllowSamples = false);

    /// <summary>Nunca se devuelve la cadena de conexión; solo servidor y base para identificarla.</summary>
    public sealed record ConnectionResponse(string Id, string Name, DatabaseEngine Engine, IReadOnlyList<string> Schemas, string? Server, string? Database,
        bool AllowSamples = false);

    /// <summary>Resultado de la prueba: tablas vistas, servidor y versión, y ajustes útiles para diagnosticar (P-028).</summary>
    /// <summary>Motor instalado, para el selector de la interfaz (incluye los cargados como plugin, P-064).</summary>
    public sealed record EngineResponse(DatabaseEngine Name, string Label, string? Example);

    public sealed record TestResult(bool Ok, int Tables, string? Error, string? Server = null,
        bool? Supported = null, IReadOnlyDictionary<string, string>? Settings = null);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,49}$")]
    private static partial Regex IdPattern();

    /// <summary>Crear, editar, probar y borrar exige el permiso ManageConnections (D-053); listar, cualquier usuario.</summary>
    private static readonly string ManageConnectionsPolicy = Auth.Permissions.Policy(Auth.Permissions.ManageConnections);

    public static void MapConnections(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections").RequireAuthorization();

        // Quien administra conexiones las ve todas; el resto, solo aquellas en que sus perfiles le dan algo (D-063).
        group.MapGet("/", async (IConnectionStore store, HttpContext http, HttpAccessContext access, CancellationToken ct) =>
        {
            var visible = await VisibleAsync(http, access, ct);
            return (await store.ListAsync(ct)).Where(c => visible(c.Id)).Select(ToResponse);
        });

        group.MapGet("/{id}", async (string id, IConnectionStore store, HttpContext http, HttpAccessContext access, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } c && (await VisibleAsync(http, access, ct))(c.Id)
                ? Results.Ok(ToResponse(c)) : Results.NotFound());

        // Fuera de /api/connections para no chocar con una conexión de id "engines".
        app.MapGet("/api/engines", (ProviderRegistry providers) =>
            providers.All.Select(p => new EngineResponse(p.Engine, p.DisplayName, p.ExampleConnectionString))).RequireAuthorization();

        // Crea o reemplaza. Si al editar no se envía la cadena de conexión, se conserva la anterior.
        group.MapPut("/{id}", async (string id, ConnectionRequest req, IConnectionStore store, SchemaService schemas,
            ProviderRegistry providers, CancellationToken ct) =>
        {
            var def = await BuildDefinitionAsync(id, req, store, providers, ct);
            await store.SaveAsync(def, ct);
            await schemas.SchemaChangedAsync(def.Id, ct);
            return Results.Ok(ToResponse(def));
        }).RequireAuthorization(ManageConnectionsPolicy);

        group.MapDelete("/{id}", async (string id, IConnectionStore store, SchemaService schemas, Core.Overrides.IProposalStore proposals, CancellationToken ct) =>
        {
            if (!await store.DeleteAsync(id, ct)) return Results.NotFound();
            // Sus ajustes se van con ella: una conexión nueva con el mismo id no los hereda.
            await schemas.Overrides.DeleteAllAsync(id, ct);
            await schemas.Snapshots.DeleteAsync(id, ct);
            await proposals.DeleteAllAsync(id, ct);
            await schemas.SchemaChangedAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(ManageConnectionsPolicy);

        // Prueba sin guardar. Un error de conexión es un resultado esperado, no una falla de la API.
        group.MapPost("/test", async (ConnectionRequest req, IConnectionStore store, SchemaService schemas,
            ProviderRegistry providers, CancellationToken ct) =>
        {
            var def = await BuildDefinitionAsync(req.Id, req, store, providers, ct);
            try
            {
                var r = await schemas.TestAsync(def, ct);
                return Results.Ok(new TestResult(
                    r.Server.IsSupported, r.Tables,
                    r.Server.IsSupported ? null : SchemaService.UnsupportedMessage(r.Server),
                    r.Server.Description, r.Server.IsSupported, r.Server.Settings));
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException or TimeoutException)
            {
                return Results.Ok(new TestResult(false, 0, ex.Message));
            }
        }).RequireAuthorization(ManageConnectionsPolicy);
    }

    private static async Task<Func<string, bool>> VisibleAsync(HttpContext http, HttpAccessContext access, CancellationToken ct)
    {
        if (await access.HasAsync(http.User, http, Auth.Permissions.ManageConnections, ct)) return _ => true;
        var mine = await access.ForAsync(http.User, http, ct);
        return mine.CanSeeConnection;
    }

    private static async Task<ConnectionDefinition> BuildDefinitionAsync(string id, ConnectionRequest req, IConnectionStore store,
        ProviderRegistry providers, CancellationToken ct)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(id))
            throw new ValidationException("id", "use solo minúsculas, números, - o _ (máx. 50)");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ValidationException("name", "es obligatorio");
        if (req.Engine.Name.Length == 0)
            throw new ValidationException("engine", "es obligatorio");
        if (!providers.Has(req.Engine))
            throw new ValidationException("engine", $"el motor {req.Engine} no está instalado");

        var connectionString = req.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = (await store.GetAsync(id, ct))?.ConnectionString
                ?? throw new ValidationException("connectionString", "es obligatoria");
        }

        try { _ = new DbConnectionStringBuilder { ConnectionString = connectionString }; }
        catch (ArgumentException) { throw new ValidationException("connectionString", "formato inválido"); }

        return new ConnectionDefinition
        {
            Id = id,
            Name = req.Name.Trim(),
            Engine = providers.Get(req.Engine).Engine, // el nombre como lo escribe el proveedor
            ConnectionString = connectionString,
            Schemas = (req.Schemas ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList(),
            AllowSamples = req.AllowSamples
        };
    }

    private static ConnectionResponse ToResponse(ConnectionDefinition c)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = c.ConnectionString };
        string? Find(params string[] keys) =>
            keys.Select(k => b.TryGetValue(k, out var v) ? v?.ToString() : null).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        return new ConnectionResponse(c.Id, c.Name, c.Engine, c.Schemas,
            Find("Server", "Data Source", "Host", "Address"),
            Find("Database", "Initial Catalog"), c.AllowSamples);
    }
}
