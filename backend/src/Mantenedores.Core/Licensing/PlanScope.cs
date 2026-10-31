using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Licensing;

/// <summary>
/// Qué conexiones y tablas deja usar el plan (P-116). Core no sabe de licencias: la API lo implementa y
/// <see cref="Services.SchemaService"/> lo aplica en el mismo lugar que los permisos, así todo el CRUD lo respeta.
/// </summary>
public interface IPlanScope
{
    ValueTask<PlanAccess> CurrentAsync(CancellationToken ct);
}

/// <summary>
/// Conexiones y tablas permitidas. <see cref="Connection"/> nulo = cualquier conexión; <see cref="Tables"/> nulo =
/// cualquier tabla. En Gratis, la conexión es una sola y las tablas son las elegidas (hasta 15, fijas).
/// </summary>
public sealed record PlanAccess(string? Connection, IReadOnlySet<string>? Tables, Plan Plan)
{
    public static readonly PlanAccess All = new(null, null, Plan.Business);

    public bool AllowsConnection(string id) => Connection is null || string.Equals(Connection, id, StringComparison.OrdinalIgnoreCase);

    public bool AllowsTable(string schema, string table) => Tables is null || Tables.Contains(Key(schema, table));

    public static string Key(string schema, string table) => $"{schema}.{table}".ToLowerInvariant();

    public void EnsureConnection(string id)
    {
        if (!AllowsConnection(id))
            throw new PlanLimitException($"El plan Gratis permite usar 1 conexión ('{Connection}'). Pase a Pro para usar '{id}'.");
    }

    public void EnsureTable(string schema, string table)
    {
        if (!AllowsTable(schema, table))
            throw new PlanLimitException(Tables!.Count == 0
                ? "Plan Gratis: elija primero sus 15 tablas en Plan."
                : $"Plan Gratis: {schema}.{table} no está entre sus 15 tablas. Pase a Pro para usar todas.");
    }
}

public sealed class NoPlanScope : IPlanScope
{
    public ValueTask<PlanAccess> CurrentAsync(CancellationToken ct) => ValueTask.FromResult(PlanAccess.All);
}
