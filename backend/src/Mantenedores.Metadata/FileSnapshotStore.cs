using System.Text.Json;
using Mantenedores.Core.Schema;

namespace Mantenedores.Metadata;

/// <summary>Fotos de referencia en archivos JSON (Metadata:Provider = File): una por conexión en <c>snapshots/</c>.</summary>
public sealed class FileSnapshotStore : ISchemaSnapshotStore
{
    private readonly string _folder;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FileSnapshotStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public async Task<StoredSnapshot?> GetAsync(string connectionId, CancellationToken ct)
    {
        var path = PathFor(connectionId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<StoredSnapshot>(await File.ReadAllTextAsync(path, ct), Json);
    }

    public async Task SaveAsync(string connectionId, SchemaSnapshot snapshot, string? user, CancellationToken ct)
    {
        var path = PathFor(connectionId);
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new StoredSnapshot(snapshot, DateTimeOffset.UtcNow, user), Json), ct);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public Task DeleteAsync(string connectionId, CancellationToken ct)
    {
        File.Delete(PathFor(connectionId));
        return Task.CompletedTask;
    }

    // El id de conexión ya está validado (minúsculas, números, - y _), así que sirve como nombre de archivo.
    private string PathFor(string connectionId) => Path.Combine(_folder, connectionId.Trim().ToLowerInvariant() + ".json");
}
