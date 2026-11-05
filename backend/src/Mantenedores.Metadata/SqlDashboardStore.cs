using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Mantenedores.Core.Dashboards;

namespace Mantenedores.Metadata;

/// <summary>Formato del documento de un dashboard: camelCase, enums como texto y sin nulos (P-128).</summary>
internal static class DashboardJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Dashboards (<c>mnt_dashboard</c>, P-128): nombre y dueño en columnas; el resto en el documento JSON.</summary>
public sealed class SqlDashboardStore(MetadataDatabase db) : IDashboardStore
{
    private const string Columns = "id AS Id, definition AS Definition, created_at AS CreatedAt";

    public async Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_dashboard ORDER BY created_at, id", cancellationToken: ct));
        return rows.Select(ToDashboard).ToList();
    }

    public async Task<Dashboard?> GetAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_dashboard WHERE id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : ToDashboard(row);
    }

    public async Task SaveAsync(Dashboard d, CancellationToken ct)
    {
        var args = new
        {
            d.Id, d.Name, d.Owner, Definition = JsonSerializer.Serialize(d, DashboardJson.Options),
            CreatedAt = IsoDate.Write(d.CreatedAt), UpdatedAt = IsoDate.Write(d.UpdatedAt)
        };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_dashboard SET name = @Name, owner = @Owner, definition = @Definition, updated_at = @UpdatedAt WHERE id = @Id",
            args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_dashboard (id, name, owner, definition, created_at, updated_at) " +
                "VALUES (@Id, @Name, @Owner, @Definition, @CreatedAt, @UpdatedAt)", args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_dashboard WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    private static Dashboard ToDashboard(Row r) =>
        JsonSerializer.Deserialize<Dashboard>(r.Definition, DashboardJson.Options)!;

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Definition { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }
}

/// <summary>Dashboards en <c>dashboards.json</c> (metadata en modo archivo).</summary>
public sealed class FileDashboardStore(string path) : IDashboardStore
{
    private readonly JsonListFile<Dashboard> _file = new(path, DashboardJson.Options);

    public async Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken ct) =>
        (await _file.ReadAsync(ct)).OrderBy(d => d.CreatedAt).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
    public async Task<Dashboard?> GetAsync(string id, CancellationToken ct) => (await _file.ReadAsync(ct)).FirstOrDefault(d => d.Id == id);
    public Task SaveAsync(Dashboard dashboard, CancellationToken ct) =>
        _file.UpdateAsync(l => [.. l.Where(d => d.Id != dashboard.Id), dashboard], ct);
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var found = false;
        await _file.UpdateAsync(l => { found = l.Any(d => d.Id == id); return l.Where(d => d.Id != id).ToList(); }, ct);
        return found;
    }
}
