using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

public static class MetaEndpoints
{
    public sealed record TableSummary(
        string Schema, string Name, TableKind Kind, bool Editable, bool HasPrimaryKey,
        string? DisplayColumn, int Columns, int ForeignKeys, int ReferencedBy);

    public sealed record SchemaSummary(string Hash, DateTimeOffset ReadAt, IReadOnlyList<TableSummary> Tables);

    public static void MapMeta(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/meta/{conn}").RequireAuthorization();

        group.MapGet("/tables", async (string conn, SchemaService schemas, CancellationToken ct) =>
            Summarize((await schemas.GetContextAsync(conn, ct)).Schema));

        // Definición completa que usa la UI para dibujar grilla y formulario.
        group.MapGet("/tables/{schema}/{table}", async (string conn, string schema, string table, SchemaService schemas, CancellationToken ct) =>
            (await schemas.GetTableAsync(conn, schema, table, ct)).Table);

        group.MapPost("/refresh", async (string conn, SchemaService schemas, CancellationToken ct) =>
            Summarize(await schemas.RefreshAsync(conn, ct)));
    }

    private static SchemaSummary Summarize(DatabaseSchema s) => new(
        s.Hash, s.ReadAt,
        s.Tables.Select(t => new TableSummary(
            t.Schema, t.Name, t.Kind, t.IsEditable, t.HasPrimaryKey, t.DisplayColumn,
            t.Columns.Count, t.ForeignKeys.Count, t.ReferencedBy.Count)).ToList());
}
