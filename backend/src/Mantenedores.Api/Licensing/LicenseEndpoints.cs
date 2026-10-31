using Mantenedores.Api.Auth;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Licensing;

/// <summary>Plan y licencia (P-116, D-106): ver el plan, pegar una clave, empezar la prueba y elegir las tablas de Gratis.</summary>
public static class LicenseEndpoints
{
    public sealed record Usage(int Users, int Connections, int Tables);

    public sealed record PlanResponse(
        Plan Plan, string Source, string? Organization, string? Email, DateTimeOffset? ExpiresAt, bool InGrace, DateTimeOffset? GraceUntil,
        string? LicenseError, DateTimeOffset? TrialEndsAt, bool TrialAvailable, PlanLimits Limits, Usage Usage,
        IReadOnlyList<string> RequiresBusiness, IReadOnlyList<string> FreeTables, string? FreeConnection, bool NeedsTableSelection,
        IReadOnlyDictionary<string, bool> Features);

    public sealed record KeyRequest(string? Key);
    public sealed record TablesRequest(List<string> Tables);
    public sealed record TableCandidate(string Schema, string Name, string Kind, string? Label, bool Selected, int ForeignKeys);
    public sealed record TablesResponse(string? Connection, int Max, IReadOnlyList<string> Selected, IReadOnlyList<TableCandidate> Tables);

    public static void MapLicense(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/license").RequireAuthorization();
        var manage = Permissions.Policy(Permissions.ManageSecurity);

        group.MapGet("/", async (LicenseService license, IConnectionStore connections, CancellationToken ct) =>
            await ResponseAsync(license, connections, ct));

        group.MapPut("/", async (KeyRequest body, LicenseService license, IConnectionStore connections, CancellationToken ct) =>
        {
            await license.SetLicenseAsync(body.Key, ct);
            return await ResponseAsync(license, connections, ct);
        }).RequireAuthorization(manage);

        group.MapDelete("/", async (LicenseService license, IConnectionStore connections, CancellationToken ct) =>
        {
            await license.SetLicenseAsync(null, ct);
            return await ResponseAsync(license, connections, ct);
        }).RequireAuthorization(manage);

        group.MapPost("/trial", async (LicenseService license, IConnectionStore connections, CancellationToken ct) =>
        {
            await license.StartTrialAsync(ct);
            return await ResponseAsync(license, connections, ct);
        }).RequireAuthorization(manage);

        // Tablas de la conexión de Gratis, para elegir (sin el filtro del plan, que justamente se está eligiendo).
        group.MapGet("/tables", async (LicenseService license, SchemaService schemas, CancellationToken ct) =>
        {
            var s = await license.GetAsync(ct);
            if (s.FreeConnection is null || s.Limits.Tables is not { } max) return new TablesResponse(s.FreeConnection, s.Limits.Tables ?? 0, s.Selection.Tables, []);
            var schema = (await schemas.GetUnrestrictedContextAsync(s.FreeConnection, ct)).Schema;
            var selected = s.Selection.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new TablesResponse(s.FreeConnection, max, s.Selection.Tables, schema.Tables
                .OrderBy(t => t.Schema).ThenBy(t => t.Name)
                .Select(t => new TableCandidate(t.Schema, t.Name, t.Kind.ToString(), t.Label, selected.Contains(PlanAccess.Key(t.Schema, t.Name)), t.ForeignKeys.Count))
                .ToList());
        }).RequireAuthorization(manage);

        group.MapPost("/tables", async (TablesRequest body, LicenseService license, SchemaService schemas, IConnectionStore connections, CancellationToken ct) =>
        {
            var s = await license.GetAsync(ct);
            var schema = s.FreeConnection is null ? null : (await schemas.GetUnrestrictedContextAsync(s.FreeConnection, ct)).Schema;
            var names = schema?.Tables.Select(t => PlanAccess.Key(t.Schema, t.Name)).ToHashSet() ?? [];
            await license.AddFreeTablesAsync(body.Tables ?? [], names.Contains, ct);
            return await ResponseAsync(license, connections, ct);
        }).RequireAuthorization(manage);
    }

    private static async Task<PlanResponse> ResponseAsync(LicenseService license, IConnectionStore connections, CancellationToken ct)
    {
        var s = await license.GetAsync(ct);
        var usage = new Usage(await license.ActiveUserCountAsync(ct), (await connections.ListAsync(ct)).Count, s.Selection.Tables.Count);
        var features = Enum.GetValues<Feature>().ToDictionary(f => char.ToLowerInvariant(f.ToString()[0]) + f.ToString()[1..], s.Limits.Has);
        return new PlanResponse(s.Plan, s.Source, s.License?.Organization, s.License?.Email, s.License?.ExpiresAt, s.InGrace, s.GraceUntil,
            s.LicenseError, s.TrialEndsAt, s.TrialAvailable, s.Limits, usage, s.RequiresBusiness, s.Selection.Tables, s.FreeConnection,
            s.NeedsTableSelection, features);
    }
}

/// <summary>
/// Lo que el plan corta antes de llegar a los endpoints (P-116): funciones del plan Empresa en uso sin esa licencia,
/// el servidor MCP sin Pro y usuarios sin lugar en el plan. Salud, login y la pantalla Plan siempre responden.
/// </summary>
public sealed class PlanGateMiddleware(RequestDelegate next)
{
    private static readonly string[] Open = ["/api/health", "/api/auth", "/api/license"];

    public async Task InvokeAsync(HttpContext http, LicenseService license, Microsoft.Extensions.Options.IOptions<AuthOptions> auth)
    {
        var path = http.Request.Path;
        if (!path.StartsWithSegments("/api") || Open.Any(p => path.StartsWithSegments(p)))
        {
            await next(http);
            return;
        }
        var ct = http.RequestAborted;
        var s = await license.GetAsync(ct);
        if (s.RequiresBusiness.Count > 0)
            throw new Core.Errors.PlanLimitException(
                $"Esta instalación usa funciones del plan Empresa: {string.Join("; ", s.RequiresBusiness)}. Active una licencia Empresa en Plan o en License:Key.");
        if (path.StartsWithSegments(Mcp.McpSetup.Path)) await license.RequireAsync(Feature.Ai, ct);
        if (auth.Value.Mode == AuthMode.Local && http.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name })
            await license.EnsureSeatAsync(name, ct);
        await next(http);
    }
}
