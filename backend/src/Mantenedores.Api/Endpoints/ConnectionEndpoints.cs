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

    public sealed record TestResult(bool Ok, int Tables, string? Error);

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,49}$")]
    private static partial Regex IdPattern();

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
        });

        group.MapDelete("/{id}", async (string id, IConnectionStore store, SchemaService schemas, CancellationToken ct) =>
        {
            schemas.Invalidate(id);
            return await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
        });

        // Prueba sin guardar. Un error de conexión es un resultado esperado, no una falla de la API.
        group.MapPost("/test", async (ConnectionRequest req, IConnectionStore store, SchemaService schemas, CancellationToken ct) =>
        {
            var def = await BuildDefinitionAsync(req.Id, req, store, ct);
            try
            {
                return Results.Ok(new TestResult(true, await schemas.TestAsync(def, ct), null));
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException or TimeoutException)
            {
                return Results.Ok(new TestResult(false, 0, ex.Message));
            }
        });
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
