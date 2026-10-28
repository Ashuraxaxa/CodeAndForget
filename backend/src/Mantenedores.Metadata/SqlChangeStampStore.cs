using Dapper;
using Mantenedores.Core.Services;

namespace Mantenedores.Metadata;

/// <summary>Contadores de cambios en la base de metadata (tabla <c>mnt_change_stamp</c>, D-077). SQL portable.</summary>
public sealed class SqlChangeStampStore(MetadataDatabase db) : IChangeStampStore
{
    public async Task<IReadOnlyDictionary<string, long>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(string Scope, long Version)>(new CommandDefinition(
            "SELECT scope, version FROM mnt_change_stamp", cancellationToken: ct));
        return rows.ToDictionary(r => r.Scope, r => r.Version, StringComparer.Ordinal);
    }

    public async Task<long> BumpAsync(string scope, CancellationToken ct)
    {
        // Dos instancias pueden crear el mismo ámbito a la vez: la que pierde la carrera reintenta con el UPDATE.
        for (var attempt = 0; ; attempt++)
        {
            await using var conn = await db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var args = new { scope, now = DateTimeOffset.UtcNow };
            try
            {
                var updated = await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE mnt_change_stamp SET version = version + 1, changed_at = @now WHERE scope = @scope", args, tx, cancellationToken: ct));
                if (updated == 0)
                    await conn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO mnt_change_stamp (scope, version, changed_at) VALUES (@scope, 1, @now)", args, tx, cancellationToken: ct));
                // Dentro de la transacción la fila sigue bloqueada: se lee la versión que dejó este UPDATE.
                var version = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT version FROM mnt_change_stamp WHERE scope = @scope", args, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                return version;
            }
            catch (System.Data.Common.DbException) when (attempt == 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
        }
    }
}
