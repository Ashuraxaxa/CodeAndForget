using System.Reflection;
using Dapper;

namespace Mantenedores.Metadata;

/// <summary>
/// Aplica los scripts de Migrations/&lt;Motor&gt;/ en orden y registra cada uno en mnt_migration.
/// Cada script corre en su propia transacción: si falla, no queda a medias. Ejecutarlo dos veces no hace nada.
/// Con varias instancias arrancando a la vez, solo una migra: las demás esperan el bloqueo y encuentran todo aplicado (P-081).
/// </summary>
public sealed class MetadataMigrator(MetadataDatabase db)
{
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);

        if (db.Provider == MetadataProvider.Sqlite)
        {
            // WAL permite leer mientras otra conexión escribe (varias solicitudes a la vez).
            await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
        }

        if (db.AcquireMigrationLockSql is { } lockSql)
            await conn.ExecuteAsync(new CommandDefinition(lockSql, commandTimeout: 180, cancellationToken: ct));
        try
        {
            return await ApplyAsync(conn, ct);
        }
        finally
        {
            if (db.ReleaseMigrationLockSql is { } unlockSql)
                await conn.ExecuteAsync(new CommandDefinition(unlockSql, cancellationToken: CancellationToken.None));
        }
    }

    private async Task<IReadOnlyList<string>> ApplyAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        await CreateMigrationTableAsync(conn, ct);
        var done = new List<string>();
        foreach (var (id, sql) in Scripts(db.MigrationsFolder))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            // Se revisa dentro de la transacción: otra instancia pudo aplicarlo mientras tanto.
            var already = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM mnt_migration WHERE id = @id", new { id }, tx, cancellationToken: ct));
            if (already > 0) { await tx.RollbackAsync(ct); continue; }
            await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_migration (id, applied_at) VALUES (@id, @at)",
                new { id, at = DateTimeOffset.UtcNow }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            done.Add(id);
        }
        return done;
    }

    /// <summary>Crea mnt_migration si falta; si otra instancia la creó al mismo tiempo (SQLite), sigue.</summary>
    private async Task CreateMigrationTableAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        try { await conn.ExecuteAsync(new CommandDefinition(db.CreateMigrationTableSql, cancellationToken: ct)); }
        catch (System.Data.Common.DbException)
        {
            if (!await TableExistsAsync(conn, ct)) throw;
        }
    }

    private static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        try { await conn.ExecuteScalarAsync(new CommandDefinition("SELECT COUNT(*) FROM mnt_migration", cancellationToken: ct)); return true; }
        catch (System.Data.Common.DbException) { return false; }
    }

    /// <summary>Scripts embebidos del motor, ordenados por nombre (001_, 002_…).</summary>
    public static IReadOnlyList<(string Id, string Sql)> Scripts(string folder)
    {
        var asm = Assembly.GetExecutingAssembly();
        var prefix = $"Migrations/{folder}/";
        return asm.GetManifestResourceNames()
            .Select(n => (Name: n.Replace('\\', '/'), Resource: n))
            .Where(n => n.Name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = asm.GetManifestResourceStream(n.Resource)!;
                using var reader = new StreamReader(stream);
                return (Path.GetFileNameWithoutExtension(n.Name), reader.ReadToEnd());
            })
            .ToList();
    }
}
