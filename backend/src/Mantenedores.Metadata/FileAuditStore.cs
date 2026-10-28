using System.Text.Json;
using Mantenedores.Core.Audit;

namespace Mantenedores.Metadata;

/// <summary>Auditoría en un archivo JSON por línea (Metadata:Provider = File), para desarrollo y pruebas.</summary>
public sealed class FileAuditStore : IAuditStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FileAuditStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public async Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var next = (await ReadAsync(ct)).Select(e => e.Id).DefaultIfEmpty(0).Max() + 1;
            await File.AppendAllLinesAsync(_path, entries.Select(e => JsonSerializer.Serialize(e with { Id = next++ }, Json)), ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<AuditPage> QueryAsync(AuditQuery q, CancellationToken ct)
    {
        static bool Same(string? a, string b) => a is null || string.Equals(a, b, StringComparison.Ordinal);
        var matches = (await ReadAsync(ct)).Where(e =>
                Same(q.Connection, e.Connection) && Same(q.Schema, e.Schema) && Same(q.Table, e.Table) &&
                (q.RowKey is null || q.RowKey == e.RowKey) && (q.User is null || q.User == e.User) &&
                (q.Operation is null || q.Operation == e.Operation) &&
                (q.From is null || e.At >= q.From) && (q.To is null || e.At < q.To) && AuditAccess.Allows(q, e))
            .OrderByDescending(e => e.Id).ToList();
        var size = Math.Clamp(q.PageSize, 1, AuditQuery.MaxPageSize);
        var page = Math.Max(q.Page, 1);
        return new AuditPage(matches.Skip((page - 1) * size).Take(size).ToList(), matches.Count, page, size);
    }

    public async Task<IReadOnlyList<AuditTable>> TablesAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Select(e => new AuditTable(e.Connection, e.Schema, e.Table)).Distinct().ToList();

    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var keep = all.Where(e => e.At >= before).ToList();
            if (keep.Count == all.Count) return 0;
            await File.WriteAllLinesAsync(_path + ".tmp", keep.Select(e => JsonSerializer.Serialize(e, Json)), ct);
            File.Move(_path + ".tmp", _path, overwrite: true);
            return all.Count - keep.Count;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<AuditEntry>> ReadAsync(CancellationToken ct) => File.Exists(_path)
        ? (await File.ReadAllLinesAsync(_path, ct)).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<AuditEntry>(l, Json)!).ToList()
        : [];
}
