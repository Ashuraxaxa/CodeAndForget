using Mantenedores.Core.Overrides;

namespace Mantenedores.IntegrationTests.Infrastructure;

public sealed class InMemoryOverrideStore : IOverrideStore
{
    private readonly Dictionary<(string, string, string), TableOverride> _items = [];

    public Task<IReadOnlyDictionary<(string Schema, string Table), TableOverride>> ListAsync(string connectionId, CancellationToken ct)
    {
        lock (_items)
            return Task.FromResult<IReadOnlyDictionary<(string, string), TableOverride>>(_items
                .Where(kv => kv.Key.Item1 == connectionId)
                .ToDictionary(kv => (kv.Key.Item2, kv.Key.Item3), kv => kv.Value));
    }

    public Task SaveAsync(string connectionId, string schema, string table, TableOverride value, string? user, CancellationToken ct)
    {
        lock (_items) _items[(connectionId, schema, table)] = value;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string connectionId, string schema, string table, CancellationToken ct)
    {
        lock (_items) return Task.FromResult(_items.Remove((connectionId, schema, table)));
    }

    public Task DeleteAllAsync(string connectionId, CancellationToken ct)
    {
        lock (_items) foreach (var k in _items.Keys.Where(k => k.Item1 == connectionId).ToList()) _items.Remove(k);
        return Task.CompletedTask;
    }
}
