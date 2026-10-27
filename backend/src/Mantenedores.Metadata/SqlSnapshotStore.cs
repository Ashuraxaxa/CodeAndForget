using System.Text.Json;
using Dapper;
using Mantenedores.Core.Schema;

namespace Mantenedores.Metadata;

/// <summary>Foto de referencia del esquema en la base de metadata (tabla <c>mnt_schema_snapshot</c>).</summary>
public sealed class SqlSnapshotStore(MetadataDatabase db) : ISchemaSnapshotStore
{
    public async Task<StoredSnapshot?> GetAsync(string connectionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT snapshot AS Snapshot, taken_at AS TakenAt, taken_by AS TakenBy FROM mnt_schema_snapshot WHERE connection_id = @id",
            new { id = Normalize(connectionId) }, cancellationToken: ct));
        return row is null ? null : new StoredSnapshot(SnapshotJson.Deserialize(row.Snapshot), ParseDate(row.TakenAt), row.TakenBy);
    }

    public async Task SaveAsync(string connectionId, SchemaSnapshot snapshot, string? user, CancellationToken ct)
    {
        var args = new { id = Normalize(connectionId), snapshot = SnapshotJson.Serialize(snapshot), now = DateTimeOffset.UtcNow, user };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_schema_snapshot SET snapshot = @snapshot, taken_at = @now, taken_by = @user WHERE connection_id = @id",
            args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_schema_snapshot (connection_id, snapshot, taken_at, taken_by) VALUES (@id, @snapshot, @now, @user)",
                args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(string connectionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_schema_snapshot WHERE connection_id = @id", new { id = Normalize(connectionId) }, cancellationToken: ct));
    }

    private static string Normalize(string id) => id.Trim().ToLowerInvariant();

    // SQLite devuelve la fecha como texto; Postgres y SQL Server, como fecha.
    private static DateTimeOffset ParseDate(object value) => value switch
    {
        DateTimeOffset d => d,
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        string s => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"Fecha inesperada: {value}")
    };

    private sealed class Row
    {
        public string Snapshot { get; set; } = "";
        public object TakenAt { get; set; } = "";
        public string? TakenBy { get; set; }
    }
}

internal static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(SchemaSnapshot s) => JsonSerializer.Serialize(s, Options);
    public static SchemaSnapshot Deserialize(string json) => JsonSerializer.Deserialize<SchemaSnapshot>(json, Options) ?? new([]);
}
