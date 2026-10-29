using System.Globalization;
using System.Text.Json;
using Dapper;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Security;

namespace Mantenedores.Metadata;

/// <summary>Fechas como texto ISO 8601 en UTC en las tablas de la IA (migración 007): mismo formato en los tres motores.</summary>
internal static class IsoDate
{
    public static string? Write(DateTimeOffset? d) => d?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    public static DateTimeOffset? Read(string? s) => s is null ? null : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

/// <summary>Tokens de acceso (<c>mnt_access_token</c>, P-113). Solo el hash del secreto.</summary>
public sealed class SqlAccessTokenStore(MetadataDatabase db) : IAccessTokenStore
{
    private const string Columns = "id AS Id, name AS Name, owner AS Owner, roles AS Roles, prefix AS Prefix, token_hash AS Hash, " +
                                   "created_at AS CreatedAt, expires_at AS ExpiresAt, last_used_at AS LastUsedAt";

    public async Task<IReadOnlyList<AccessToken>> ListAsync(string? owner, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_access_token" + (owner is null ? "" : " WHERE owner = @owner") + " ORDER BY created_at",
            new { owner = owner?.ToLowerInvariant() }, cancellationToken: ct));
        return rows.Select(ToToken).ToList();
    }

    public async Task<AccessToken?> FindByHashAsync(string hash, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_access_token WHERE token_hash = @hash", new { hash }, cancellationToken: ct));
        return row is null ? null : ToToken(row);
    }

    public async Task SaveAsync(AccessToken t, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO mnt_access_token (id, name, owner, roles, prefix, token_hash, created_at, expires_at, last_used_at) " +
            "VALUES (@Id, @Name, @Owner, @Roles, @Prefix, @Hash, @CreatedAt, @ExpiresAt, NULL)",
            new
            {
                t.Id, t.Name, Owner = t.Owner.ToLowerInvariant(), Roles = JsonSerializer.Serialize(t.Roles), t.Prefix, t.Hash,
                CreatedAt = IsoDate.Write(t.CreatedAt), ExpiresAt = IsoDate.Write(t.ExpiresAt)
            }, cancellationToken: ct));
    }

    public async Task<bool> DeleteAsync(string id, string? owner, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_access_token WHERE id = @id" + (owner is null ? "" : " AND owner = @owner"),
            new { id, owner = owner?.ToLowerInvariant() }, cancellationToken: ct)) > 0;
    }

    public async Task TouchAsync(string id, DateTimeOffset at, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_access_token SET last_used_at = @at WHERE id = @id", new { id, at = IsoDate.Write(at) }, cancellationToken: ct));
    }

    private static AccessToken ToToken(Row r) => new()
    {
        Id = r.Id, Name = r.Name, Owner = r.Owner, Prefix = r.Prefix, Hash = r.Hash,
        Roles = JsonSerializer.Deserialize<List<string>>(r.Roles) ?? [],
        CreatedAt = IsoDate.Read(r.CreatedAt) ?? default, ExpiresAt = IsoDate.Read(r.ExpiresAt), LastUsedAt = IsoDate.Read(r.LastUsedAt)
    };

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Roles { get; set; } = "[]";
        public string Prefix { get; set; } = "";
        public string Hash { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string? ExpiresAt { get; set; }
        public string? LastUsedAt { get; set; }
    }
}

/// <summary>Propuestas de ajustes de una IA (<c>mnt_override_proposal</c>, P-113).</summary>
public sealed class SqlProposalStore(MetadataDatabase db) : IProposalStore
{
    private const string Columns = "id AS Id, connection_id AS Connection, schema_name AS SchemaName, table_name AS TableName, " +
                                   "settings AS Settings, reason AS Reason, created_by AS CreatedBy, created_at AS CreatedAt, status AS Status, " +
                                   "resolved_by AS ResolvedBy, resolved_at AS ResolvedAt";

    public async Task<IReadOnlyList<OverrideProposal>> ListAsync(string connection, string? status, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_override_proposal WHERE connection_id = @connection" +
            (status is null ? "" : " AND status = @status") + " ORDER BY created_at",
            new { connection = connection.ToLowerInvariant(), status }, cancellationToken: ct));
        return rows.Select(ToProposal).ToList();
    }

    public async Task<OverrideProposal?> GetAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM mnt_override_proposal WHERE id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : ToProposal(row);
    }

    public async Task SaveAsync(OverrideProposal p, CancellationToken ct)
    {
        var args = new
        {
            p.Id, Connection = p.Connection.ToLowerInvariant(), p.Schema, p.Table, Settings = OverrideJson.Serialize(p.Value), p.Reason,
            p.CreatedBy, CreatedAt = IsoDate.Write(p.CreatedAt), p.Status, p.ResolvedBy, ResolvedAt = IsoDate.Write(p.ResolvedAt)
        };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_override_proposal SET settings = @Settings, reason = @Reason, status = @Status, resolved_by = @ResolvedBy, " +
            "resolved_at = @ResolvedAt WHERE id = @Id", args, tx, cancellationToken: ct));
        if (updated == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_override_proposal (id, connection_id, schema_name, table_name, settings, reason, created_by, created_at, status, resolved_by, resolved_at) " +
                "VALUES (@Id, @Connection, @Schema, @Table, @Settings, @Reason, @CreatedBy, @CreatedAt, @Status, @ResolvedBy, @ResolvedAt)",
                args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAllAsync(string connection, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_override_proposal WHERE connection_id = @connection", new { connection = connection.ToLowerInvariant() }, cancellationToken: ct));
    }

    private static OverrideProposal ToProposal(Row r) => new()
    {
        Id = r.Id, Connection = r.Connection, Schema = r.SchemaName, Table = r.TableName, Value = OverrideJson.Deserialize(r.Settings),
        Reason = r.Reason, CreatedBy = r.CreatedBy, CreatedAt = IsoDate.Read(r.CreatedAt) ?? default, Status = r.Status,
        ResolvedBy = r.ResolvedBy, ResolvedAt = IsoDate.Read(r.ResolvedAt)
    };

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Connection { get; set; } = "";
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Settings { get; set; } = "{}";
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public string CreatedAt { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ResolvedBy { get; set; }
        public string? ResolvedAt { get; set; }
    }
}
