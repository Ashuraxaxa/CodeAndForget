using System.Reflection;
using Dapper;

namespace Mantenedores.Metadata;

/// <summary>
/// Aplica los scripts de Migrations/&lt;Motor&gt;/ en orden y registra cada uno en mnt_migration.
/// Cada script corre en su propia transacción: si falla, no queda a medias. Ejecutarlo dos veces no hace nada.
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

        await conn.ExecuteAsync(new CommandDefinition(db.CreateMigrationTableSql, cancellationToken: ct));
        var applied = (await conn.QueryAsync<string>(new CommandDefinition("SELECT id FROM mnt_migration", cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var done = new List<string>();
        foreach (var (id, sql) in Scripts(db.MigrationsFolder).Where(s => !applied.Contains(s.Id)))
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_migration (id, applied_at) VALUES (@id, @at)",
                new { id, at = DateTimeOffset.UtcNow }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            done.Add(id);
        }
        return done;
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
