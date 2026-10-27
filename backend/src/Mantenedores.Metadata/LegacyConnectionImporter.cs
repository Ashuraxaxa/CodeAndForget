using System.Text.Json;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Microsoft.AspNetCore.DataProtection;

namespace Mantenedores.Metadata;

/// <summary>
/// Pasa las conexiones del archivo de la Fase 1 (data/connections.json) a la base de metadata, una sola vez:
/// solo si el archivo existe y la tabla está vacía. Descifra con las llaves de data/keys y vuelve a cifrar
/// con las llaves activas (que pueden estar en la base). Al terminar renombra el archivo a *.importado.
/// </summary>
public static class LegacyConnectionImporter
{
    public static async Task<int> ImportAsync(string dataDirectory, SqlConnectionStore store, string applicationName, CancellationToken ct = default)
    {
        var file = Path.Combine(dataDirectory, "connections.json");
        if (!File.Exists(file) || !await store.IsEmptyAsync(ct)) return 0;

        var legacy = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(dataDirectory, "keys")),
            b => b.SetApplicationName(applicationName));
        var protector = legacy.CreateProtector(SqlConnectionStore.ProtectorPurpose);

        await using var stream = File.OpenRead(file);
        var entries = await JsonSerializer.DeserializeAsync<List<Entry>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? [];
        await stream.DisposeAsync();

        foreach (var e in entries)
        {
            await store.SaveAsync(new ConnectionDefinition
            {
                Id = e.Id,
                Name = e.Name,
                Engine = e.Engine,
                Schemas = e.Schemas,
                ConnectionString = protector.Unprotect(e.ProtectedConnectionString)
            }, ct);
        }

        File.Move(file, file + ".importado", overwrite: true);
        return entries.Count;
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
