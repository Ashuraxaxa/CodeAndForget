using Dapper;
using Mantenedores.Core.Overrides;

namespace Mantenedores.Metadata;

/// <summary>
/// Ajustes en la base de metadata (tabla <c>mnt_table_override</c>). Esquema y tabla se guardan tal como los
/// devuelve el catálogo; el id de conexión, en minúsculas como en <see cref="SqlConnectionStore"/>.
/// </summary>
public sealed class SqlOverrideStore(MetadataDatabase db) : IOverrideStore
{
    public async Task<IReadOnlyDictionary<(string Schema, string Table), TableOverride>> ListAsync(string connectionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            "SELECT schema_name AS SchemaName, table_name AS TableName, settings AS Settings " +
            "FROM mnt_table_override WHERE connection_id = @id", new { id = Normalize(connectionId) }, cancellationToken: ct));
        return rows.ToDictionary(r => (r.SchemaName, r.TableName), r => OverrideJson.Deserialize(r.Settings));
    }

    public async Task SaveAsync(string connectionId, string schema, string table, TableOverride value, string? user, CancellationToken ct)
    {
        var args = new
        {
            id = Normalize(connectionId),
            schema,
            table,
            settings = OverrideJson.Serialize(value),
            now = DateTimeOffset.UtcNow,
            user
        };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_table_override SET settings = @settings, updated_at = @now, updated_by = @user " +
            "WHERE connection_id = @id AND schema_name = @schema AND table_name = @table", args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_table_override (connection_id, schema_name, table_name, settings, updated_at, updated_by) " +
                "VALUES (@id, @schema, @table, @settings, @now, @user)", args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteAsync(string connectionId, string schema, string table, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_table_override WHERE connection_id = @id AND schema_name = @schema AND table_name = @table",
            new { id = Normalize(connectionId), schema, table }, cancellationToken: ct)) > 0;
    }

    public async Task DeleteAllAsync(string connectionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_table_override WHERE connection_id = @id", new { id = Normalize(connectionId) }, cancellationToken: ct));
    }

    private static string Normalize(string id) => id.Trim().ToLowerInvariant();

    private sealed class Row
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Settings { get; set; } = "{}";
    }
}
