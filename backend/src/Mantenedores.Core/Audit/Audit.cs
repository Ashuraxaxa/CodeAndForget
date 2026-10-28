using System.Globalization;
using System.Text.Json;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Audit;

/// <summary>Qué cambió (D-065). Las lecturas no se registran.</summary>
public enum AuditOperation { Create, Update, Delete, Import, Links, Action }

/// <summary>
/// Un cambio en los datos: quién, cuándo, dónde, la clave del registro y los valores antes y después
/// (JSON con solo las columnas que cambiaron; en altas, lo que quedó; en bajas, lo que había).
/// </summary>
public sealed record AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset At { get; init; }
    public string? User { get; init; }
    public required string Connection { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public AuditOperation Operation { get; init; }

    /// <summary>Clave del registro como <c>col=valor&amp;col2=valor2</c> (ordenada por columna); null si no se conoce (importación con identity).</summary>
    public string? RowKey { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
}

public sealed record AuditQuery
{
    public string? Connection { get; init; }
    public string? Schema { get; init; }
    public string? Table { get; init; }
    public string? RowKey { get; init; }
    public string? User { get; init; }
    public AuditOperation? Operation { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Lo que el usuario puede ver (P-095): conexiones completas y tablas sueltas. Ambas null = sin restricción;
    /// listas vacías = nada.
    /// </summary>
    public IReadOnlyList<string>? AllowedConnections { get; init; }
    public IReadOnlyList<AuditTable>? AllowedTables { get; init; }

    /// <summary>Contar el total (la exportación recorre páginas y no lo necesita).</summary>
    public bool CountTotal { get; init; } = true;
}

/// <summary>Una tabla que tiene entradas en la auditoría.</summary>
public sealed record AuditTable(string Connection, string Schema, string Table);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, long Total, int Page, int PageSize);

public interface IAuditStore
{
    Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct);
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct);

    /// <summary>Las tablas distintas que tienen entradas (para saber cuáles puede ver un usuario con perfil).</summary>
    Task<IReadOnlyList<AuditTable>> TablesAsync(CancellationToken ct);

    /// <summary>Borra lo anterior a <paramref name="before"/>; devuelve cuántas entradas borró.</summary>
    Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct);
}

/// <summary>Sin almacén (pruebas): no registra nada.</summary>
public sealed class NoAuditStore : IAuditStore
{
    public Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct) => Task.CompletedTask;
    public Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct) => Task.FromResult(new AuditPage([], 0, 1, query.PageSize));
    public Task<IReadOnlyList<AuditTable>> TablesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AuditTable>>([]);
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>Configuración (<c>Audit</c>), D-065.</summary>
public sealed class AuditOptions
{
    public const string Section = "Audit";

    /// <summary>Registrar los cambios. Por defecto, sí.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Días que se guardan las entradas; 0 o menos = para siempre.</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Cada cuántas horas se borran las vencidas.</summary>
    public int PurgeIntervalHours { get; set; } = 24;
}

/// <summary>Arma las entradas a partir de las filas de la tabla, con las mismas reglas para todos los motores.</summary>
public static class AuditValues
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Clave estable para buscar el historial de un registro: columnas de la PK ordenadas, valores en texto invariante.</summary>
    public static string Key(IEnumerable<KeyValuePair<string, string?>> pk) =>
        string.Join("&", pk.OrderBy(kv => kv.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key.ToLowerInvariant())}={Uri.EscapeDataString(kv.Value ?? "")}"));

    public static string? Key(TableInfo table, IReadOnlyDictionary<string, object?> row) =>
        table.PrimaryKey.Count == 0 || table.PrimaryKey.Any(c => Get(row, c) is null)
            ? null
            : Key(table.PrimaryKey.Select(c => new KeyValuePair<string, string?>(c, Text(Get(row, c)))));

    /// <summary>Solo columnas reales y visibles para quien escribe, sin la rowversion; los binarios se anotan por tamaño.</summary>
    public static Dictionary<string, object?> Columns(TableInfo table, IReadOnlyDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>();
        // La rowversion cambia en cada edición y no dice nada del dato: no se registra.
        foreach (var c in table.Columns.Where(c => (!c.Hidden || c.IsPrimaryKey) && !c.IsRowVersion))
        {
            if (c.Type == LogicalType.Binary)
            {
                if (TryGet(row, c.Name, out var bin) && bin is byte[] bytes) result[c.Name] = $"({bytes.Length} bytes)";
                else if (TryGet(row, c.Name + Query.QueryBuilder.SizeSuffix, out var size) && size is not null) result[c.Name] = $"({size} bytes)";
                continue;
            }
            if (TryGet(row, c.Name, out var v)) result[c.Name] = v;
        }
        return result;
    }

    /// <summary>Antes y después solo con lo que cambió. Null si no cambió nada.</summary>
    public static (string Old, string New)? Diff(TableInfo table, IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        var a = Columns(table, before);
        var b = Columns(table, after);
        var oldV = new Dictionary<string, object?>();
        var newV = new Dictionary<string, object?>();
        foreach (var name in a.Keys.Union(b.Keys))
        {
            a.TryGetValue(name, out var x);
            b.TryGetValue(name, out var y);
            if (Serialize(x) == Serialize(y)) continue;
            oldV[name] = x;
            newV[name] = y;
        }
        return newV.Count == 0 ? null : (Serialize(oldV), Serialize(newV));
    }

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Json);

    private static string? Text(object? v) => v switch
    {
        null => null,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)
    };

    private static object? Get(IReadOnlyDictionary<string, object?> row, string name) => TryGet(row, name, out var v) ? v : null;

    private static bool TryGet(IReadOnlyDictionary<string, object?> row, string name, out object? value)
    {
        if (row.TryGetValue(name, out value)) return true;
        foreach (var kv in row)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; return true; }
        value = null;
        return false;
    }
}

/// <summary>
/// Registra los cambios después de confirmarlos en la base de datos. Si la auditoría falla, el cambio ya está hecho:
/// se informa con <c>onError</c> (el log de la API) en vez de devolver un error que haría creer que no se guardó (D-068).
/// </summary>
public sealed class AuditService(IAuditStore store, AuditOptions options, Action<Exception>? onError = null)
{
    public IAuditStore Store => store;
    public bool Enabled => options.Enabled;

    public async Task RecordAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        if (!options.Enabled || entries.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        try
        {
            // El cambio ya se confirmó: que una cancelación de la solicitud no deje el registro sin auditar.
            await store.WriteAsync(entries.Select(e => e with { At = now }).ToList(), CancellationToken.None);
        }
        catch (Exception ex) when (onError is not null)
        {
            onError(ex);
        }
    }

    public Task<int> PurgeExpiredAsync(CancellationToken ct) =>
        options.RetentionDays <= 0 ? Task.FromResult(0) : store.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-options.RetentionDays), ct);
}
