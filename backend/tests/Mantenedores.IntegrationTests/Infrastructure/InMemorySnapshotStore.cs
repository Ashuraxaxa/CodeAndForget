using Mantenedores.Core.Schema;

namespace Mantenedores.IntegrationTests.Infrastructure;

public sealed class InMemorySnapshotStore : ISchemaSnapshotStore
{
    private readonly Dictionary<string, StoredSnapshot> _items = new(StringComparer.OrdinalIgnoreCase);

    public Task<StoredSnapshot?> GetAsync(string connectionId, CancellationToken ct)
    {
        lock (_items) return Task.FromResult(_items.GetValueOrDefault(connectionId));
    }

    public Task SaveAsync(string connectionId, SchemaSnapshot snapshot, string? user, CancellationToken ct)
    {
        lock (_items) _items[connectionId] = new StoredSnapshot(snapshot, DateTimeOffset.UtcNow, user);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string connectionId, CancellationToken ct)
    {
        lock (_items) _items.Remove(connectionId);
        return Task.CompletedTask;
    }
}
