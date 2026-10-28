using System.Globalization;
using System.Text;
using Dapper;
using Mantenedores.Core.Audit;

namespace Mantenedores.Metadata;

/// <summary>
/// Auditoría en la base de metadata (tabla <c>mnt_audit</c>). En SQLite la fecha se guarda como texto ISO en UTC,
/// que se ordena y compara igual que la fecha.
/// </summary>
public sealed class SqlAuditStore(MetadataDatabase db) : IAuditStore
{
    private bool Sqlite => db.Provider == MetadataProvider.Sqlite;

    private object At(DateTimeOffset at) =>
        Sqlite ? at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) : at.ToUniversalTime();

    public async Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO mnt_audit (at, username, connection_id, schema_name, table_name, operation, row_key, old_values, new_values) " +
            "VALUES (@At, @User, @Connection, @Schema, @Table, @Operation, @RowKey, @OldValues, @NewValues)",
            entries.Select(e => new
            {
                At = At(e.At), e.User, e.Connection, e.Schema, e.Table, Operation = e.Operation.ToString(),
                RowKey = Truncate(e.RowKey), e.OldValues, e.NewValues
            }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<AuditPage> QueryAsync(AuditQuery q, CancellationToken ct)
    {
        var where = new StringBuilder(" WHERE 1 = 1");
        var args = new DynamicParameters();
        void Eq(string column, string name, object? value)
        {
            if (value is null) return;
            where.Append($" AND {column} = @{name}");
            args.Add(name, value);
        }
        Eq("connection_id", "conn", q.Connection);
        Eq("schema_name", "schema", q.Schema);
        Eq("table_name", "tbl", q.Table);
        Eq("row_key", "rowKey", Truncate(q.RowKey));
        Eq("username", "usr", q.User);
        Eq("operation", "op", q.Operation?.ToString());
        if (q.From is { } from) { where.Append(" AND at >= @from"); args.Add("from", At(from)); }
        if (q.To is { } to) { where.Append(" AND at < @to"); args.Add("to", At(to)); }
        // Perfil (P-095): conexiones completas o tablas sueltas que el usuario puede ver.
        if (q.AllowedConnections is not null || q.AllowedTables is not null)
        {
            var allowed = new List<string>();
            var conns = q.AllowedConnections ?? [];
            if (conns.Count > 0)
            {
                allowed.Add($"connection_id IN ({string.Join(", ", conns.Select((_, i) => $"@ac{i}"))})");
                for (var i = 0; i < conns.Count; i++) args.Add($"ac{i}", conns[i]);
            }
            var tables = q.AllowedTables ?? [];
            for (var i = 0; i < tables.Count; i++)
            {
                allowed.Add($"(connection_id = @tc{i} AND schema_name = @ts{i} AND table_name = @tt{i})");
                args.Add($"tc{i}", tables[i].Connection); args.Add($"ts{i}", tables[i].Schema); args.Add($"tt{i}", tables[i].Table);
            }
            where.Append(allowed.Count == 0 ? " AND 1 = 0" : $" AND ({string.Join(" OR ", allowed)})");
        }

        var size = Math.Clamp(q.PageSize, 1, AuditQuery.MaxPageSize);
        var page = Math.Max(q.Page, 1);
        args.Add("skip", (page - 1) * size);
        args.Add("take", size);
        // El id crece con el tiempo: ordenar por él da lo más reciente primero sin depender del formato de la fecha.
        var paging = Sqlite ? " ORDER BY id DESC LIMIT @take OFFSET @skip" : " ORDER BY id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

        await using var conn = await db.OpenAsync(ct);
        var total = q.CountTotal
            ? await conn.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM mnt_audit" + where, args, cancellationToken: ct))
            : -1;
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, at AS At, username AS Username, connection_id AS ConnectionId, schema_name AS SchemaName, table_name AS TableName, " +
            "operation AS Operation, row_key AS RowKey, old_values AS OldValues, new_values AS NewValues FROM mnt_audit" + where + paging,
            args, cancellationToken: ct));
        return new AuditPage(rows.Select(r => new AuditEntry
        {
            Id = r.Id, At = ParseDate(r.At), User = r.Username, Connection = r.ConnectionId, Schema = r.SchemaName, Table = r.TableName,
            Operation = Enum.TryParse<AuditOperation>(r.Operation, out var op) ? op : AuditOperation.Update,
            RowKey = r.RowKey, OldValues = r.OldValues, NewValues = r.NewValues
        }).ToList(), total, page, size);
    }

    public async Task<IReadOnlyList<AuditTable>> TablesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var rows = await conn.QueryAsync<(string Connection, string Schema, string Table)>(new CommandDefinition(
            "SELECT DISTINCT connection_id, schema_name, table_name FROM mnt_audit", cancellationToken: ct));
        return rows.Select(r => new AuditTable(r.Connection, r.Schema, r.Table)).ToList();
    }

    public async Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("DELETE FROM mnt_audit WHERE at < @before",
            new { before = At(before) }, commandTimeout: 300, cancellationToken: ct));
    }

    /// <summary>El índice de SQL Server tiene un tope de largo: una clave más larga se recorta (igual al guardar y al buscar).</summary>
    private static string? Truncate(string? key) => key is { Length: > 400 } ? key[..400] : key;

    private static DateTimeOffset ParseDate(object value) => value switch
    {
        DateTimeOffset d => d,
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        _ => throw new InvalidOperationException($"Fecha inesperada: {value}")
    };

    private sealed class Row
    {
        public long Id { get; set; }
        public object At { get; set; } = "";
        public string? Username { get; set; }
        public string ConnectionId { get; set; } = "";
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Operation { get; set; } = "";
        public string? RowKey { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
    }
}
