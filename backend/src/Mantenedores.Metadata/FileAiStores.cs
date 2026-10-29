using System.Text.Json;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Security;

namespace Mantenedores.Metadata;

/// <summary>Lista guardada en un archivo JSON (modo File, una sola instancia): para tokens y propuestas.</summary>
internal sealed class JsonListFile<T>(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<T>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var s = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(s, Json, ct) ?? [];
    }

    public async Task UpdateAsync(Func<List<T>, List<T>> change, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var items = change(await ReadAsync(ct));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(items, Json), ct);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { _lock.Release(); }
    }
}

public sealed class FileAccessTokenStore(string path) : IAccessTokenStore
{
    private readonly JsonListFile<AccessToken> _file = new(path);
    private static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<AccessToken>> ListAsync(string? owner, CancellationToken ct) =>
        (await _file.ReadAsync(ct)).Where(t => owner is null || Same(t.Owner, owner)).ToList();
    public async Task<AccessToken?> FindByHashAsync(string hash, CancellationToken ct) =>
        (await _file.ReadAsync(ct)).FirstOrDefault(t => t.Hash == hash);
    public Task SaveAsync(AccessToken token, CancellationToken ct) => _file.UpdateAsync(l => [.. l, token], ct);
    public async Task<bool> DeleteAsync(string id, string? owner, CancellationToken ct)
    {
        var removed = false;
        await _file.UpdateAsync(l => { removed = l.RemoveAll(t => t.Id == id && (owner is null || Same(t.Owner, owner))) > 0; return l; }, ct);
        return removed;
    }
    public Task TouchAsync(string id, DateTimeOffset at, CancellationToken ct) =>
        _file.UpdateAsync(l => l.Select(t => t.Id == id ? t with { LastUsedAt = at } : t).ToList(), ct);
}

public sealed class FileProposalStore(string path) : IProposalStore
{
    private readonly JsonListFile<OverrideProposal> _file = new(path);

    public async Task<IReadOnlyList<OverrideProposal>> ListAsync(string connection, string? status, CancellationToken ct) =>
        (await _file.ReadAsync(ct)).Where(p => string.Equals(p.Connection, connection, StringComparison.OrdinalIgnoreCase) && (status is null || p.Status == status))
            .OrderBy(p => p.CreatedAt).ToList();
    public async Task<OverrideProposal?> GetAsync(string id, CancellationToken ct) => (await _file.ReadAsync(ct)).FirstOrDefault(p => p.Id == id);
    public Task SaveAsync(OverrideProposal proposal, CancellationToken ct) =>
        _file.UpdateAsync(l => [.. l.Where(p => p.Id != proposal.Id), proposal], ct);
    public Task DeleteAllAsync(string connection, CancellationToken ct) =>
        _file.UpdateAsync(l => l.Where(p => !string.Equals(p.Connection, connection, StringComparison.OrdinalIgnoreCase)).ToList(), ct);
}
