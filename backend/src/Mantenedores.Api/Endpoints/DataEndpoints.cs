using System.Text.Json;
using Mantenedores.Core.Query;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Services;
using Mantenedores.Core.Transfer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

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

        // Relación N:M (P-015): valores vinculados [{ value, label }] y reemplazo de la lista completa en una transacción.
        group.MapGet("/row/links/{link}", (string conn, string schema, string table, string link, HttpRequest http,
            CrudService crud, CancellationToken ct) =>
            crud.GetLinksAsync(conn, schema, table, Key(http.Query), link, ct));

        group.MapPut("/row/links/{link}", (string conn, string schema, string table, string link, HttpRequest http,
            List<System.Text.Json.JsonElement> values, CrudService crud, CancellationToken ct) =>
            crud.SetLinksAsync(conn, schema, table, Key(http.Query), link, values, ct));

        // Archivo de una columna binaria (P-018). Se sube en base64 en el alta o la edición; aquí se descarga.
        group.MapGet("/row/file/{column}", async (string conn, string schema, string table, string column, HttpRequest http,
            CrudService crud, CancellationToken ct) =>
        {
            var key = Key(http.Query);
            key.Remove("download");
            var file = await crud.GetFileAsync(conn, schema, table, key, column, ct);
            if (file is null) return Results.NotFound();
            // Imágenes y PDF se pueden ver en el navegador; lo demás se descarga.
            var inline = http.Query["download"] != "1" && (file.ContentType.StartsWith("image/") || file.ContentType == "application/pdf");
            return inline
                ? Results.File(file.Content, file.ContentType)
                : Results.File(file.Content, file.ContentType, file.FileName);
        });

        // Árbol de una tabla con autorreferencia (P-016): un nivel por llamada. Sin ?parent, las raíces.
        group.MapGet("/tree", (string conn, string schema, string table, string? parent, int? take, CrudService crud, CancellationToken ct) =>
            crud.TreeAsync(conn, schema, table, parent, take ?? 500, ct));

        // ---------- Exportar e importar (P-017) ----------

        // ?format=csv|xlsx|json|xml con los mismos filtros, búsqueda y orden del listado (sin paginar).
        group.MapGet("/export", async (string conn, string schema, string table, string? format, HttpContext http,
            CrudService crud, CancellationToken ct) =>
        {
            var plan = await crud.PrepareExportAsync(conn, schema, table, ParseList(http.Request.Query),
                TransferFormats.Parse(format ?? "csv"), ct);
            http.Response.ContentType = TransferFormats.ContentType(plan.Format);
            http.Response.Headers[HeaderNames.ContentDisposition] =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = plan.FileName }.ToString();
            http.Response.Headers["X-Total-Count"] = plan.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await plan.WriteAsync(http.Response.Body, ct);
            return Results.Empty;
        });

        // Multipart: "file" (csv, xlsx, json o xml, por su extensión) y "mapping" opcional ({"encabezado": "columna" | null}).
        group.MapPost("/import/preview", async (string conn, string schema, string table, HttpRequest http,
            CrudService crud, CrudOptions options, CancellationToken ct) =>
        {
            var (file, mapping) = await ReadImportAsync(http, options, ct);
            await using var stream = file.OpenReadStream();
            return Results.Ok(await crud.PreviewImportAsync(conn, schema, table, stream, TransferFormats.FromFileName(file.FileName), mapping, ct));
        });

        // Igual que la vista previa, pero graba si no hay errores. Con errores responde 400 con el mismo detalle.
        group.MapPost("/import", async (string conn, string schema, string table, HttpRequest http,
            CrudService crud, CrudOptions options, CancellationToken ct) =>
        {
            var (file, mapping) = await ReadImportAsync(http, options, ct);
            await using var stream = file.OpenReadStream();
            var result = await crud.ImportAsync(conn, schema, table, stream, TransferFormats.FromFileName(file.FileName), mapping, ct);
            return result.Imported > 0 || (result.Errors.Count == 0 && result.TotalRows == 0)
                ? Results.Ok(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
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

    private static async Task<(IFormFile File, Dictionary<string, string?>? Mapping)> ReadImportAsync(
        HttpRequest http, CrudOptions options, CancellationToken ct)
    {
        var maxBytes = (long)Math.Max(1, options.ImportMaxMegabytes) * 1024 * 1024;
        // El límite de Kestrel (30 MB) se ajusta al configurado, con margen para el resto del formulario.
        if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = maxBytes + 1024 * 1024;
        if (!http.HasFormContentType) throw new ValidationException("file", "envíe el archivo como multipart/form-data");

        var form = await http.ReadFormAsync(ct);
        var file = form.Files["file"] ?? throw new ValidationException("file", "falta el archivo");
        if (file.Length > maxBytes)
            throw new ValidationException("file", $"el archivo pesa más de {options.ImportMaxMegabytes} MB (Crud:ImportMaxMegabytes)");

        Dictionary<string, string?>? mapping = null;
        if (form["mapping"].ToString() is { Length: > 0 } json)
        {
            try { mapping = JsonSerializer.Deserialize<Dictionary<string, string?>>(json); }
            catch (JsonException) { throw new ValidationException("mapping", "debe ser un objeto JSON {\"encabezado\": \"columna\"}"); }
        }
        return (file, mapping);
    }

    private static Dictionary<string, string> Key(IQueryCollection q) =>
        q.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
