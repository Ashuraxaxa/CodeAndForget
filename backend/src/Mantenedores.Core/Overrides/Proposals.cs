namespace Mantenedores.Core.Overrides;

/// <summary>Estado de una propuesta de ajuste.</summary>
public static class ProposalStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Discarded = "discarded";
}

/// <summary>
/// Ajuste propuesto por una IA (P-113, D-100): no se aplica solo. Alguien con <c>ConfigureTables</c> lo carga en la
/// edición, lo ve en la vista previa y lo guarda (queda aceptado) o lo descarta.
/// </summary>
public sealed record OverrideProposal
{
    public required string Id { get; init; }
    public required string Connection { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    /// <summary>El ajuste completo de la tabla tal como quedaría.</summary>
    public required TableOverride Value { get; init; }
    /// <summary>Por qué lo propone (lo escribe la IA).</summary>
    public string? Reason { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Status { get; init; } = ProposalStatus.Pending;
    public string? ResolvedBy { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}

public interface IProposalStore
{
    Task<IReadOnlyList<OverrideProposal>> ListAsync(string connection, string? status, CancellationToken ct);
    Task<OverrideProposal?> GetAsync(string id, CancellationToken ct);
    Task SaveAsync(OverrideProposal proposal, CancellationToken ct);
    /// <summary>Al borrar una conexión.</summary>
    Task DeleteAllAsync(string connection, CancellationToken ct);
}

public sealed class NoProposalStore : IProposalStore
{
    public Task<IReadOnlyList<OverrideProposal>> ListAsync(string connection, string? status, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OverrideProposal>>([]);
    public Task<OverrideProposal?> GetAsync(string id, CancellationToken ct) => Task.FromResult<OverrideProposal?>(null);
    public Task SaveAsync(OverrideProposal proposal, CancellationToken ct) => throw new NotSupportedException("Este almacén no guarda propuestas.");
    public Task DeleteAllAsync(string connection, CancellationToken ct) => Task.CompletedTask;
}
