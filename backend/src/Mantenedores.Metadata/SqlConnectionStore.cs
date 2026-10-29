using System.Text.Json;
using Dapper;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Providers;
using Microsoft.AspNetCore.DataProtection;

namespace Mantenedores.Metadata;

/// <summary>
/// Conexiones en la base de metadata (D-023). La cadena de conexión se guarda cifrada con Data Protection,
/// con el mismo propósito que el almacén de archivo, así ambos son intercambiables.
/// El SQL es portable entre SQLite, PostgreSQL y SQL Server (UPDATE y, si no hubo fila, INSERT).
/// </summary>
public sealed class SqlConnectionStore(MetadataDatabase db, IDataProtectionProvider dataProtection) : IConnectionStore
{
    public const string ProtectorPurpose = "Mantenedores.ConnectionStrings.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public async Task<IReadOnlyList<ConnectionDefinition>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, engine AS Engine, protected_connection_string AS Protected, schemas AS Schemas, allow_samples AS AllowSamples " +
            "FROM mnt_connection ORDER BY id", cancellationToken: ct));
        return rows.Select(ToDefinition).ToList();
    }

    public async Task<ConnectionDefinition?> GetAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, engine AS Engine, protected_connection_string AS Protected, schemas AS Schemas, allow_samples AS AllowSamples " +
            "FROM mnt_connection WHERE id = @id", new { id = Normalize(id) }, cancellationToken: ct));
        return row is null ? null : ToDefinition(row);
    }

    public async Task SaveAsync(ConnectionDefinition connection, CancellationToken ct)
    {
        var args = new
        {
            id = Normalize(connection.Id),
            name = connection.Name,
            engine = connection.Engine.ToString(),
            prot = _protector.Protect(connection.ConnectionString),
            schemas = JsonSerializer.Serialize(connection.Schemas),
            samples = connection.AllowSamples ? 1 : 0,
            now = DateTimeOffset.UtcNow
        };

        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE mnt_connection SET name = @name, engine = @engine, protected_connection_string = @prot, " +
            "schemas = @schemas, allow_samples = @samples, updated_at = @now WHERE id = @id", args, tx, cancellationToken: ct));
        if (updated == 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mnt_connection (id, name, engine, protected_connection_string, schemas, allow_samples, created_at, updated_at) " +
                "VALUES (@id, @name, @engine, @prot, @schemas, @samples, @now, @now)", args, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM mnt_connection WHERE id = @id", new { id = Normalize(id) }, cancellationToken: ct)) > 0;
    }

    /// <summary>¿Hay alguna conexión? (para decidir si se importa el archivo de la Fase 1).</summary>
    public async Task<bool> IsEmptyAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM mnt_connection", cancellationToken: ct)) == 0;
    }

    /// <summary>Los identificadores se guardan en minúsculas: la búsqueda no depende de la intercalación del motor.</summary>
    private static string Normalize(string id) => id.Trim().ToLowerInvariant();

    private ConnectionDefinition ToDefinition(Row r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Engine = DatabaseEngine.Parse(r.Engine),
        ConnectionString = _protector.Unprotect(r.Protected),
        Schemas = JsonSerializer.Deserialize<List<string>>(r.Schemas) ?? [],
        AllowSamples = r.AllowSamples != 0
    };

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Engine { get; set; } = "";
        public string Protected { get; set; } = "";
        public string Schemas { get; set; } = "[]";
        public long AllowSamples { get; set; }
    }
}
