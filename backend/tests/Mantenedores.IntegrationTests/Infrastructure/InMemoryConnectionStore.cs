using Mantenedores.Core.Connections;

namespace Mantenedores.IntegrationTests.Infrastructure;

public sealed class InMemoryConnectionStore(params ConnectionDefinition[] initial) : IConnectionStore
{
    private readonly Dictionary<string, ConnectionDefinition> _items =
        initial.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ConnectionDefinition>>(_items.Values.ToList());

    public Task<ConnectionDefinition?> GetAsync(string id, CancellationToken ct) =>
        Task.FromResult(_items.GetValueOrDefault(id));

    public Task SaveAsync(ConnectionDefinition connection, CancellationToken ct)
    {
        _items[connection.Id] = connection;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(_items.Remove(id));
}
