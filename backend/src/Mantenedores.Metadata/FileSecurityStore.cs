using System.Text.Json;
using Mantenedores.Core.Security;

namespace Mantenedores.Metadata;

/// <summary>Perfiles y usuarios en un archivo JSON (Metadata:Provider = File), para desarrollo y pruebas.</summary>
public sealed class FileSecurityStore : ISecurityStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileSecurityStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public async Task<IReadOnlyList<SecurityProfile>> ListProfilesAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Profiles.OrderBy(p => p.Name).ToList();

    public Task SaveProfileAsync(SecurityProfile profile, string? user, CancellationToken ct) => Update(d =>
    {
        var p = profile with { Id = Normalize(profile.Id) };
        d.Profiles.RemoveAll(x => x.Id == p.Id);
        d.Profiles.Add(p);
        return true;
    }, ct);

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken ct)
    {
        var removed = false;
        await Update(d => removed = d.Profiles.RemoveAll(x => x.Id == Normalize(id)) > 0, ct);
        return removed;
    }

    public async Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Users.OrderBy(u => u.Username).ToList();

    public async Task<LocalUser?> GetUserAsync(string username, CancellationToken ct) =>
        (await ReadAsync(ct)).Users.FirstOrDefault(u => u.Username == Normalize(username));

    public Task SaveUserAsync(LocalUser user, CancellationToken ct) => Update(d =>
    {
        var u = user with { Username = Normalize(user.Username) };
        d.Users.RemoveAll(x => x.Username == u.Username);
        d.Users.Add(u);
        return true;
    }, ct);

    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct)
    {
        var removed = false;
        await Update(d => removed = d.Users.RemoveAll(x => x.Username == Normalize(username)) > 0, ct);
        return removed;
    }

    private async Task Update(Func<Document, bool> change, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var doc = await ReadAsync(ct);
            if (!change(doc)) return;
            await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(doc, Json), ct);
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        finally { _lock.Release(); }
    }

    private async Task<Document> ReadAsync(CancellationToken ct) =>
        File.Exists(_path) ? JsonSerializer.Deserialize<Document>(await File.ReadAllTextAsync(_path, ct), Json) ?? new() : new();

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();

    private sealed class Document
    {
        public List<SecurityProfile> Profiles { get; set; } = [];
        public List<LocalUser> Users { get; set; } = [];
    }
}
