using System.Text.Json;
using Dapper;
using Mantenedores.Core.Security;

namespace Mantenedores.Metadata;

/// <summary>Perfiles y usuarios propios en la base de metadata (D-062). Nombres de usuario e ids en minúsculas.</summary>
public sealed class SqlSecurityStore(MetadataDatabase db) : ISecurityStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SecurityProfile>> ListProfilesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition("SELECT settings FROM mnt_profile ORDER BY name", cancellationToken: ct));
        return rows.Select(r => JsonSerializer.Deserialize<SecurityProfile>(r, Json)!).ToList();
    }

    public async Task SaveProfileAsync(SecurityProfile profile, string? user, CancellationToken ct)
    {
        var p = profile with { Id = Normalize(profile.Id) };
        var args = new { id = p.Id, name = p.Name, settings = JsonSerializer.Serialize(p, Json), now = DateTimeOffset.UtcNow, user };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE mnt_profile SET name = @name, settings = @settings, updated_at = @now, updated_by = @user WHERE id = @id",
                args, tx, cancellationToken: ct)) == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_profile (id, name, settings, updated_at, updated_by) VALUES (@id, @name, @settings, @now, @user)",
                args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_profile WHERE id = @id",
            new { id = Normalize(id) }, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(
            "SELECT username AS Username, display_name AS DisplayName, password_hash AS PasswordHash, active AS Active FROM mnt_user ORDER BY username",
            cancellationToken: ct));
        return rows.Select(ToUser).ToList();
    }

    public async Task<LocalUser?> GetUserAsync(string username, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            "SELECT username AS Username, display_name AS DisplayName, password_hash AS PasswordHash, active AS Active FROM mnt_user WHERE username = @u",
            new { u = Normalize(username) }, cancellationToken: ct));
        return row is null ? null : ToUser(row);
    }

    public async Task SaveUserAsync(LocalUser user, CancellationToken ct)
    {
        var args = new { u = Normalize(user.Username), d = user.DisplayName, h = user.PasswordHash, a = user.Active, now = DateTimeOffset.UtcNow };
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE mnt_user SET display_name = @d, password_hash = @h, active = @a, updated_at = @now WHERE username = @u",
                args, tx, cancellationToken: ct)) == 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_user (username, display_name, password_hash, active, updated_at) VALUES (@u, @d, @h, @a, @now)",
                args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteUserAsync(string username, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_user WHERE username = @u",
            new { u = Normalize(username) }, cancellationToken: ct)) > 0;
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();

    private static LocalUser ToUser(UserRow r) => new()
    {
        Username = r.Username, DisplayName = r.DisplayName, PasswordHash = r.PasswordHash, Active = Convert.ToBoolean(r.Active)
    };

    private sealed class UserRow
    {
        public string Username { get; set; } = "";
        public string? DisplayName { get; set; }
        public string PasswordHash { get; set; } = "";
        public object Active { get; set; } = true; // bool en Postgres/SQL Server, entero en SQLite
    }
}
