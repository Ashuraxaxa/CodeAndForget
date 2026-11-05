using Mantenedores.Core.Dashboards;

namespace Mantenedores.Api.Endpoints;

/// <summary>
/// Dashboards (P-128, D-111). Los permisos, el compartir y los topes del plan se aplican en <see cref="DashboardService"/>;
/// los datos de cada tarjeta salen con los permisos de quien mira. Los datos no usan el tope de consultas pesadas
/// (un dashboard pide varias tarjetas a la vez): en tablas grandes los protege la cola por conexión del CRUD.
/// </summary>
public static class DashboardEndpoints
{
    public sealed record DashboardSummary(string Id, string Name, string? Description, string Owner, int Widgets,
        DateTimeOffset UpdatedAt, bool Shared, bool CanEdit, bool CanDelete, bool CanShare, bool Locked);
    public sealed record ListResponse(IReadOnlyList<DashboardSummary> Items, bool CanCreate, int Used, int? Limit, int WidgetLimit);
    public sealed record DataRequest(string? From, string? To);
    public sealed record PreviewRequest(DashboardWidget Widget, string? From, string? To);

    public static void MapDashboards(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards").RequireAuthorization();

        group.MapGet("/", async (DashboardService dashboards, CancellationToken ct) =>
        {
            var list = await dashboards.ListAsync(ct);
            return new ListResponse(list.Items.Select(Summary).ToList(), list.CanCreate, list.Used, list.Limit, list.WidgetLimit);
        });

        group.MapGet("/{id}", (string id, DashboardService dashboards, CancellationToken ct) => dashboards.GetAsync(id, ct));

        group.MapPost("/", (DashboardInput body, DashboardService dashboards, CancellationToken ct) => dashboards.CreateAsync(body, ct));

        group.MapPut("/{id}", (string id, DashboardInput body, DashboardService dashboards, CancellationToken ct) =>
            dashboards.UpdateAsync(id, body, ct));

        group.MapDelete("/{id}", async (string id, DashboardService dashboards, CancellationToken ct) =>
        {
            await dashboards.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id}/widgets/{widget}/data", (string id, string widget, DataRequest? body, DashboardService dashboards, CancellationToken ct) =>
            dashboards.WidgetDataAsync(id, widget, body?.From, body?.To, ct));

        group.MapPost("/preview", (PreviewRequest body, DashboardService dashboards, CancellationToken ct) =>
            dashboards.PreviewAsync(body.Widget, body.From, body.To, ct));
    }

    private static DashboardSummary Summary(DashboardView v) => new(
        v.Dashboard.Id, v.Dashboard.Name, v.Dashboard.Description, v.Dashboard.Owner, v.Dashboard.Widgets.Count,
        v.Dashboard.UpdatedAt, v.Dashboard.Shares.Count > 0, v.CanEdit, v.CanDelete, v.CanShare, v.Locked);
}
