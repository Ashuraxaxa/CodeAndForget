using System.Security.Claims;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

public static class MetaEndpoints
{
    public sealed record TableSummary(
        string Schema, string Name, TableKind Kind, bool Editable, bool HasPrimaryKey,
        string? DisplayColumn, int Columns, int ForeignKeys, int ReferencedBy,
        string? Label, string? Group, bool Hidden);

    /// <summary><c>Warnings</c>: ajustes que no se pudieron aplicar (p. ej. una columna borrada de la base).</summary>
    public sealed record SchemaSummary(string Hash, DateTimeOffset ReadAt, IReadOnlyList<TableSummary> Tables, IReadOnlyList<string> Warnings);

    public sealed record OverrideEntry(string Schema, string Table, TableOverride Settings);

    public static void MapMeta(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/meta/{conn}").RequireAuthorization();

        group.MapGet("/tables", async (string conn, SchemaService schemas, CancellationToken ct) =>
            Summarize((await schemas.GetContextAsync(conn, ct)).Schema));

        // Definición completa que usa la UI para dibujar grilla y formulario (con los ajustes aplicados).
        group.MapGet("/tables/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
            (await schemas.GetTableAsync(conn, schema, table, ct)).Table);

        group.MapPost("/refresh", async (string conn, SchemaService schemas, CancellationToken ct) =>
            Summarize(await schemas.RefreshAsync(conn, ct)));

        // --- Ajustes por tabla (P-011). Leer: cualquier usuario; cambiar: solo administradores (D-050). ---

        group.MapGet("/overrides", async (string conn, SchemaService schemas, CancellationToken ct) =>
        {
            await schemas.GetRawSchemaAsync(conn, ct); // 404 si la conexión no existe
            var all = await schemas.Overrides.ListAsync(conn, ct);
            return all.Select(kv => new OverrideEntry(kv.Key.Schema, kv.Key.Table, kv.Value))
                .OrderBy(e => e.Schema).ThenBy(e => e.Table).ToList();
        });

        group.MapGet("/overrides/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
        {
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            return await FindAsync(schemas, conn, info, ct) ?? new TableOverride();
        });

        // Reemplaza el ajuste completo de la tabla y devuelve la tabla ya combinada (sirve de vista previa).
        group.MapPut("/overrides/{schema}/{table}", async (string conn, string schema, string table, TableOverride body,
            SchemaService schemas, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var raw = await schemas.GetRawSchemaAsync(conn, ct);
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            OverrideApplier.Validate(raw, info.Schema, info.Name, body);

            await RemoveVariantsAsync(schemas, conn, info, ct);
            await schemas.Overrides.SaveAsync(conn, info.Schema, info.Name, body, user.Identity?.Name, ct);
            schemas.InvalidateOverrides(conn);
            return (await schemas.GetTableAsync(conn, info.Schema, info.Name, ct)).Table;
        }).RequireAuthorization(AuthSetup.AdminPolicy);

        group.MapDelete("/overrides/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
        {
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            var removed = await RemoveVariantsAsync(schemas, conn, info, ct);
            schemas.InvalidateOverrides(conn);
            return removed ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(AuthSetup.AdminPolicy);
    }

    private static async Task<TableInfo> RawTableAsync(SchemaService schemas, string conn, string schema, string table, CancellationToken ct) =>
        (await schemas.GetRawSchemaAsync(conn, ct)).FindTable(schema, table)
        ?? throw new Core.Errors.NotFoundException($"La tabla '{schema}.{table}' no existe en '{conn}'.");

    private static async Task<TableOverride?> FindAsync(SchemaService schemas, string conn, TableInfo info, CancellationToken ct) =>
        (await schemas.Overrides.ListAsync(conn, ct))
        .Where(kv => Same(kv.Key.Schema, info.Schema) && Same(kv.Key.Table, info.Name))
        .Select(kv => kv.Value).FirstOrDefault();

    /// <summary>
    /// Borra el ajuste de la tabla, también si quedó guardado con otras mayúsculas (la URL no distingue,
    /// la clave del almacén sí). Así nunca hay dos ajustes para la misma tabla.
    /// </summary>
    private static async Task<bool> RemoveVariantsAsync(SchemaService schemas, string conn, TableInfo info, CancellationToken ct)
    {
        var removed = false;
        foreach (var key in (await schemas.Overrides.ListAsync(conn, ct)).Keys
                     .Where(k => Same(k.Schema, info.Schema) && Same(k.Table, info.Name)))
            removed |= await schemas.Overrides.DeleteAsync(conn, key.Schema, key.Table, ct);
        return removed;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static SchemaSummary Summarize(DatabaseSchema s) => new(
        s.Hash, s.ReadAt,
        s.Tables.Select(t => new TableSummary(
            t.Schema, t.Name, t.Kind, t.IsEditable, t.HasPrimaryKey, t.DisplayColumn,
            t.Columns.Count, t.ForeignKeys.Count, t.ReferencedBy.Count,
            t.Label, t.Group, t.Hidden)).ToList(),
        s.OverrideWarnings);
}
