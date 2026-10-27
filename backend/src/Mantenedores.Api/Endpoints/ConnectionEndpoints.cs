using System.Data.Common;
using System.Text.RegularExpressions;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

public static partial class ConnectionEndpoints
{
    public sealed record ConnectionRequest(string Id, string Name, DatabaseEngine Engine, string? ConnectionString, List<string>? Schemas);

    /// <summary>Nunca se devuelve la cadena de conexión; solo servidor y base para identificarla.</summary>
    public sealed record ConnectionResponse(string Id, string Name, DatabaseEngine Engine, IReadOnlyList<string> Schemas, string? Server, string? Database);

    /// <summary>Resultado de la prueba: tablas vistas, servidor y versión, y ajustes útiles para diagnosticar (P-028).</summary>
    public sealed record TestResult(bool Ok, int Tables, string? Error, string? Server = null,
        bool? Supported = null, IReadOnlyDictionary<string, string>? Settings = null);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,49}$")]
    private static partial Regex IdPattern();

    /// <summary>Crear, editar, probar y borrar exige el permiso ManageConnections (D-053); listar, cualquier usuario.</summary>
    private static readonly string ManageConnectionsPolicy = Auth.Permissions.Policy(Auth.Permissions.ManageConnections);

    public static void MapConnections(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections").RequireAuthorization();

        group.MapGet("/", async (IConnectionStore store, CancellationToken ct) =>
            (await store.ListAsync(ct)).Select(ToResponse));

        group.MapGet("/{id}", async (string id, IConnectionStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } c ? Results.Ok(ToResponse(c)) : Results.NotFound());

        // Crea o reemplaza. Si al editar no se envía la cadena de conexión, se conserva la anterior.
        group.MapPut("/{id}", async (string id, ConnectionRequest req, IConnectionStore store, SchemaService schemas, CancellationToken ct) =>
        {
            var def = await BuildDefinitionAsync(id, req, store, ct);
            await store.SaveAsync(def, ct);
            schemas.Invalidate(def.Id);
            return Results.Ok(ToResponse(def));
        }).RequireAuthorization(ManageConnectionsPolicy);

        group.MapDelete("/{id}", async (string id, IConnectionStore store, SchemaService schemas, CancellationToken ct) =>
        {
            schemas.Invalidate(id);
            if (!await store.DeleteAsync(id, ct)) return Results.NotFound();
            // Sus ajustes se van con ella: una conexión nueva con el mismo id no los hereda.
            await schemas.Overrides.DeleteAllAsync(id, ct);
            await schemas.Snapshots.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(ManageConnectionsPolicy);

        // Prueba sin guardar. Un error de conexión es un resultado esperado, no una falla de la API.
        group.MapPost("/test", async (ConnectionRequest req, IConnectionStore store, SchemaService schemas, CancellationToken ct) =>
        {
            var def = await BuildDefinitionAsync(req.Id, req, store, ct);
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

    private static async Task<ConnectionDefinition> BuildDefinitionAsync(string id, ConnectionRequest req, IConnectionStore store, CancellationToken ct)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(id))
            throw new ValidationException("id", "use solo minúsculas, números, - o _ (máx. 50)");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ValidationException("name", "es obligatorio");

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
            Engine = req.Engine,
            ConnectionString = connectionString,
            Schemas = (req.Schemas ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList()
        };
    }

    private static ConnectionResponse ToResponse(ConnectionDefinition c)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = c.ConnectionString };
        string? Find(params string[] keys) =>
            keys.Select(k => b.TryGetValue(k, out var v) ? v?.ToString() : null).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        return new ConnectionResponse(c.Id, c.Name, c.Engine, c.Schemas,
            Find("Server", "Data Source", "Host", "Address"),
            Find("Database", "Initial Catalog"));
    }
}
