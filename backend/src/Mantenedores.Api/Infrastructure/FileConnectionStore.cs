using System.Text.Json;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Microsoft.AspNetCore.DataProtection;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Guarda las conexiones en un archivo JSON con la cadena de conexión cifrada (ASP.NET Data Protection).
/// Suficiente para Fase 1; luego se reemplaza por el metadata store en Postgres y un vault para secretos.
/// </summary>
public sealed class FileConnectionStore : IConnectionStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileConnectionStore(string path, IDataProtectionProvider dataProtection)
    {
        _path = path;
        _protector = dataProtection.CreateProtector("Mantenedores.ConnectionStrings.v1");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public async Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Select(ToDefinition).ToList();

    public async Task<ConnectionDefinition?> GetAsync(string id, CancellationToken ct) =>
        (await ReadAsync(ct)).Where(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            .Select(ToDefinition).FirstOrDefault();

    public async Task SaveAsync(ConnectionDefinition connection, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await ReadAsync(ct);
            entries.RemoveAll(e => string.Equals(e.Id, connection.Id, StringComparison.OrdinalIgnoreCase));
            entries.Add(new Entry
            {
                Id = connection.Id,
                Name = connection.Name,
                Engine = connection.Engine,
                Schemas = connection.Schemas.ToList(),
                ProtectedConnectionString = _protector.Protect(connection.ConnectionString)
            });
            await WriteAsync(entries, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var entries = await ReadAsync(ct);
            var removed = entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await WriteAsync(entries, ct);
            return removed;
        }
        finally { _lock.Release(); }
    }

    private ConnectionDefinition ToDefinition(Entry e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Engine = e.Engine,
        Schemas = e.Schemas,
        ConnectionString = _protector.Unprotect(e.ProtectedConnectionString)
    };

    private async Task<List<Entry>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<Entry>>(stream, Json, ct) ?? [];
    }

    private async Task WriteAsync(List<Entry> entries, CancellationToken ct)
    {
        // Escritura atómica: archivo temporal y luego reemplazo.
        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, entries.OrderBy(e => e.Id).ToList(), Json, ct);
        File.Move(tmp, _path, overwrite: true);
    }

    private sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DatabaseEngine Engine { get; set; }
        public List<string> Schemas { get; set; } = [];
        public string ProtectedConnectionString { get; set; } = "";
    }
}
