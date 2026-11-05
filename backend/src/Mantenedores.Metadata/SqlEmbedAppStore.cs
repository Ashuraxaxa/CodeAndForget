using System.Text.Json;
using Dapper;
using Mantenedores.Core.Embed;

namespace Mantenedores.Metadata;

/// <summary>Aplicaciones que embeben (<c>mnt_embed_app</c>, P-135): el nombre en una columna, el resto en el documento JSON.</summary>
public sealed class SqlEmbedAppStore(MetadataDatabase db) : IEmbedAppStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<EmbedApp>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT definition FROM mnt_embed_app ORDER BY created_at, id", cancellationToken: ct));
        return rows.Select(r => JsonSerializer.Deserialize<EmbedApp>(r, Json)!).ToList();
    }

    public async Task SaveAsync(EmbedApp a, CancellationToken ct)
    {
        var args = new
        {
            a.Id, a.Name, Definition = JsonSerializer.Serialize(a, Json),
            CreatedAt = IsoDate.Write(a.CreatedAt), UpdatedAt = IsoDate.Write(a.UpdatedAt)
        };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_embed_app SET name = @Name, definition = @Definition, updated_at = @UpdatedAt WHERE id = @Id",
            args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_embed_app (id, name, definition, created_at, updated_at) VALUES (@Id, @Name, @Definition, @CreatedAt, @UpdatedAt)",
                args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_embed_app WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}

/// <summary>Aplicaciones que embeben en <c>embed-apps.json</c> (metadata en modo archivo).</summary>
public sealed class FileEmbedAppStore(string path) : IEmbedAppStore
{
    private readonly JsonListFile<EmbedApp> _file = new(path, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public async Task<IReadOnlyList<EmbedApp>> ListAsync(CancellationToken ct) =>
        (await _file.ReadAsync(ct)).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    public Task SaveAsync(EmbedApp app, CancellationToken ct) =>
        _file.UpdateAsync(l => [.. l.Where(a => a.Id != app.Id), app], ct);
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var found = false;
        await _file.UpdateAsync(l => { found = l.Any(a => a.Id == id); return l.Where(a => a.Id != id).ToList(); }, ct);
        return found;
    }
}
