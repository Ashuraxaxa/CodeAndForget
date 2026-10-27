using System.Text.Json;
using Mantenedores.Core.Query;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

/// <summary>
/// CRUD genérico. La clave de la fila va en la query string con el nombre de cada columna de la PK,
/// así funciona igual con claves simples y compuestas: /row?PedidoId=10&amp;Linea=2
/// </summary>
public static class DataEndpoints
{
    private const string FilterPrefix = "f.";

    /// <summary>Igualdad exacta: <c>?e.pedido_id=5</c> (P-014).</summary>
    private const string ExactPrefix = "e.";

    public static void MapData(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data/{conn}/{schema}/{table}").RequireAuthorization();

        // ?page=1&pageSize=25&sort=Nombre,-FechaAlta&q=texto&f.CategoriaId=3
        group.MapGet("/", (string conn, string schema, string table, HttpRequest http, CrudService crud, CancellationToken ct) =>
            crud.ListAsync(conn, schema, table, ParseList(http.Query), ct));

        group.MapGet("/row", (string conn, string schema, string table, HttpRequest http, CrudService crud, CancellationToken ct) =>
            crud.GetAsync(conn, schema, table, Key(http.Query), ct));

        group.MapPost("/", async (string conn, string schema, string table, Dictionary<string, JsonElement> body, CrudService crud, CancellationToken ct) =>
            Results.Ok(await crud.CreateAsync(conn, schema, table, body, ct)));

        group.MapPut("/row", (string conn, string schema, string table, HttpRequest http, Dictionary<string, JsonElement> body, CrudService crud, CancellationToken ct) =>
            crud.UpdateAsync(conn, schema, table, Key(http.Query), body, ct));

        group.MapDelete("/row", async (string conn, string schema, string table, HttpRequest http, CrudService crud, CancellationToken ct) =>
        {
            await crud.DeleteAsync(conn, schema, table, Key(http.Query), ct);
            return Results.NoContent();
        });

        // Opciones para combos de FK: [{ value, label }]
        group.MapGet("/lookup", (string conn, string schema, string table, string? q, int? take, CrudService crud, CancellationToken ct) =>
            crud.LookupAsync(conn, schema, table, q, take ?? 20, ct));
    }

    private static ListRequest ParseList(IQueryCollection q)
    {
        int Int(string name, int fallback) => int.TryParse(q[name], out var v) ? v : fallback;

        var sort = SortSpec.ParseList(q["sort"].ToString());

        var filters = q.Where(kv => kv.Key.StartsWith(FilterPrefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[FilterPrefix.Length..], kv => kv.Value.ToString());

        return new ListRequest
        {
            Page = Int("page", 1),
            PageSize = Int("pageSize", 25),
            Sort = sort,
            Filters = filters,
            Exact = q.Where(kv => kv.Key.StartsWith(ExactPrefix, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key[ExactPrefix.Length..], kv => kv.Value.ToString()),
            Search = q["q"].ToString()
        };
    }

    private static Dictionary<string, string> Key(IQueryCollection q) =>
        q.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
