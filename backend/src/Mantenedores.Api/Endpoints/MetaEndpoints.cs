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
        string? Label, string? Group, bool Hidden, IReadOnlyList<string> Access);

    /// <summary><c>Warnings</c>: ajustes que no se pudieron aplicar (p. ej. una columna borrada de la base).</summary>
    public sealed record SchemaSummary(string Hash, DateTimeOffset ReadAt, IReadOnlyList<TableSummary> Tables, IReadOnlyList<string> Warnings);

    public sealed record OverrideEntry(string Schema, string Table, TableOverride Settings);

    /// <summary>Tabla con el ajuste aplicado y los errores por campo (vacío = se puede guardar).</summary>
    public sealed record OverridePreview(TableInfo Table, IReadOnlyDictionary<string, string[]> Errors);

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

        // Cambios del esquema desde la foto de referencia (P-013). Aceptarlos exige ConfigureTables (D-054).
        group.MapGet("/diff", (string conn, SchemaService schemas, CancellationToken ct) => schemas.GetDiffAsync(conn, ct));

        group.MapPost("/diff/accept", (string conn, SchemaService schemas, ClaimsPrincipal user, CancellationToken ct) =>
            schemas.AcceptSchemaAsync(conn, user.Identity?.Name, ct))
            .RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));

        // --- Ajustes por tabla (P-011). Leer y cambiar: permiso ConfigureTables (D-053); nombran tablas y columnas
        // que un perfil puede ocultar (D-067). Lo que el usuario necesita ya viene aplicado en /tables. ---

        group.MapGet("/overrides", async (string conn, SchemaService schemas, CancellationToken ct) =>
        {
            await schemas.GetRawSchemaAsync(conn, ct); // 404 si la conexión no existe
            var all = await schemas.Overrides.ListAsync(conn, ct);
            return all.Select(kv => new OverrideEntry(kv.Key.Schema, kv.Key.Table, kv.Value))
                .OrderBy(e => e.Schema).ThenBy(e => e.Table).ToList();
        }).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));

        group.MapGet("/overrides/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
        {
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            return await FindAsync(schemas, conn, info, ct) ?? new TableOverride();
        }).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));

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
        }).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));

        // Vista previa para el editor (P-012): aplica el ajuste sin guardarlo. Los errores no cortan la vista previa:
        // se devuelven junto con la tabla combinada con lo que sí se pudo aplicar.
        group.MapPost("/overrides/{schema}/{table}/preview", async (string conn, string schema, string table, TableOverride body,
            SchemaService schemas, CancellationToken ct) =>
        {
            var raw = await schemas.GetRawSchemaAsync(conn, ct);
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            var others = (await schemas.Overrides.ListAsync(conn, ct))
                .Where(kv => !(Same(kv.Key.Schema, info.Schema) && Same(kv.Key.Table, info.Name)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            others[(info.Schema, info.Name)] = body;

            var errors = new Dictionary<string, string[]>();
            try { OverrideApplier.Validate(raw, info.Schema, info.Name, body); }
            catch (Core.Errors.ValidationException ex) { foreach (var (k, v) in ex.Errors) errors[k] = [v]; }

            return new OverridePreview(OverrideApplier.Apply(raw, others).FindTable(info.Schema, info.Name)!, errors);
        }).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));

        group.MapDelete("/overrides/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
        {
            var info = await RawTableAsync(schemas, conn, schema, table, ct);
            var removed = await RemoveVariantsAsync(schemas, conn, info, ct);
            schemas.InvalidateOverrides(conn);
            return removed ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(Permissions.Policy(Permissions.ConfigureTables));
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
            t.Label, t.Group, t.Hidden, Mantenedores.Core.Security.DataOperations.ToNames(t.Access))).ToList(),
        s.OverrideWarnings);
}
