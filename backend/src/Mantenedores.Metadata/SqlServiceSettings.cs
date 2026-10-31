using Dapper;
using Mantenedores.Core.Licensing;

namespace Mantenedores.Metadata;

/// <summary>Ajustes del servicio en <c>mnt_setting</c> (migración 008, P-116).</summary>
public sealed class SqlServiceSettings(MetadataDatabase db) : IServiceSettings
{
    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT value FROM mnt_setting WHERE name = @name", new { name = name.ToLowerInvariant() }, cancellationToken: ct));
    }

    public async Task SetAsync(string name, string? value, CancellationToken ct)
    {
        var args = new { name = name.ToLowerInvariant(), value, at = IsoDate.Write(DateTimeOffset.UtcNow) };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (value is null)
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_setting WHERE name = @name", args, tx, cancellationToken: ct));
        else if (await conn.ExecuteAsync(new CommandDefinition(
                     "UPDATE mnt_setting SET value = @value, updated_at = @at WHERE name = @name", args, tx, cancellationToken: ct)) == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_setting (name, value, updated_at) VALUES (@name, @value, @at)", args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}

/// <summary>Ajustes del servicio en <c>settings.json</c> (metadata en archivos).</summary>
public sealed class FileServiceSettings(string path) : IServiceSettings
{
    private sealed record Entry(string Name, string Value);
    private readonly JsonListFile<Entry> _file = new(path);

    public async Task<string?> GetAsync(string name, CancellationToken ct) =>
        (await _file.ReadAsync(ct)).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    public Task SetAsync(string name, string? value, CancellationToken ct) =>
        _file.UpdateAsync(l =>
        {
            var rest = l.Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (value is not null) rest.Add(new Entry(name.ToLowerInvariant(), value));
            return rest;
        }, ct);
}
