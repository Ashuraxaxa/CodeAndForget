using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Concurrency;

/// <summary>
/// Versión de una fila para la concurrencia optimista (P-046, D-069): un hash de los valores de las columnas que el
/// usuario ve, tal como se leen. Sirve igual en todos los motores, con o sin <c>rowversion</c>/<c>xmin</c>, y sin
/// tocar la estructura de la base del cliente.
/// </summary>
public static class RowVersion
{
    /// <summary>Campo que acompaña a cada fila en las respuestas.</summary>
    public const string Field = "__version";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Of(TableInfo table, IDictionary<string, object?> row)
    {
        var sb = new StringBuilder();
        foreach (var c in table.Columns.Where(c => c.IsSelectable || c.IsFile).OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            // Los archivos no viajan en la fila: su tamaño basta para notar que cambiaron.
            var name = c.IsFile ? c.Name + QueryBuilder.SizeSuffix : c.Name;
            sb.Append(c.Name).Append('=').Append(Text(Get(row, name))).Append('\u0001');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash, 0, 12).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Agrega la versión a la fila que se devuelve.</summary>
    public static IDictionary<string, object?> Stamp(TableInfo table, IDictionary<string, object?> row)
    {
        if (table.HasPrimaryKey) row[Field] = Of(table, row);
        return row;
    }

    private static string Text(object? v) => v switch
    {
        null => "\u0000",
        string s => s,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(v, Json)
    };

    private static object? Get(IDictionary<string, object?> row, string name)
    {
        if (row.TryGetValue(name, out var v)) return v;
        foreach (var kv in row)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }
}
