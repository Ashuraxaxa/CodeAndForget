using System.Text.Json;
using Mantenedores.Core.Overrides;

namespace Mantenedores.Metadata;

/// <summary>Ajustes en un archivo JSON (Metadata:Provider = File), para desarrollo y pruebas.</summary>
public sealed class FileOverrideStore : IOverrideStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(OverrideJson.Options) { WriteIndented = true };

    public FileOverrideStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public async Task<IReadOnlyDictionary<(string Schema, string Table), TableOverride>> ListAsync(string connectionId, CancellationToken ct) =>
        (await ReadAsync(ct)).Where(e => Same(e.Connection, connectionId))
            .ToDictionary(e => (e.Schema, e.Table), e => e.Settings);

    public Task SaveAsync(string connectionId, string schema, string table, TableOverride value, string? user, CancellationToken ct) =>
        Update(entries =>
        {
            entries.RemoveAll(e => Matches(e, connectionId, schema, table));
            entries.Add(new Entry
            {
                Connection = connectionId.Trim().ToLowerInvariant(), Schema = schema, Table = table, Settings = value,
                UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = user
            });
            return true;
        }, ct);

    public async Task<bool> DeleteAsync(string connectionId, string schema, string table, CancellationToken ct)
    {
        var removed = false;
        await Update(entries => removed = entries.RemoveAll(e => Matches(e, connectionId, schema, table)) > 0, ct);
        return removed;
    }

    public Task DeleteAllAsync(string connectionId, CancellationToken ct) =>
        Update(entries => entries.RemoveAll(e => Same(e.Connection, connectionId)) > 0, ct);

    private async Task Update(Func<List<Entry>, bool> change, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await ReadAsync(ct);
            if (!change(entries)) return;
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(entries, Json), ct);
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _lock.Release(); }
    }

    private async Task<List<Entry>> ReadAsync(CancellationToken ct) =>
        File.Exists(_path)
            ? JsonSerializer.Deserialize<List<Entry>>(await File.ReadAllTextAsync(_path, ct), Json) ?? []
            : [];

    private static bool Matches(Entry e, string connection, string schema, string table) =>
        Same(e.Connection, connection) && Same(e.Schema, schema) && Same(e.Table, table);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public string Connection { get; set; } = "";
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public TableOverride Settings { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}
