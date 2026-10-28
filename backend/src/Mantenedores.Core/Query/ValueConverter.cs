using System.Globalization;
using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Query;

/// <summary>Convierte valores de la API (JSON o texto de la URL) al tipo CLR que espera cada columna.</summary>
public static partial class ValueConverter
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^[+-]?\d+(\.\d+)?$")]
    private static partial System.Text.RegularExpressions.Regex LargeNumber();

    public static object? FromJson(ColumnInfo column, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => CheckNull(column),
        JsonValueKind.String => FromString(column, value.GetString()),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False when column.Type != LogicalType.Json
            => FromString(column, value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText()
            }),
        _ when column.Type == LogicalType.Json => value.GetRawText(),
        JsonValueKind.Array when column.Type == LogicalType.Array => FromArray(column, value),
        _ => throw Invalid(column, "formato no soportado")
    };

    /// <summary>
    /// Valor de un alta o una edición: convierte y además aplica las reglas de los ajustes (P-011):
    /// obligatorio, patrón, lista de opciones, mínimo y máximo. Los filtros y las claves usan <see cref="FromString"/>, sin reglas.
    /// </summary>
    public static object? ForWrite(ColumnInfo column, JsonElement value)
    {
        var result = FromJson(column, value);
        switch (result)
        {
            case string s when column.Type is LogicalType.String or LogicalType.Text:
                if (column.ForcedRequired && s.Trim().Length == 0) throw Invalid(column, "es obligatorio");
                if (s.Length == 0) break;
                if (column.Options is { Count: > 0 } options && !options.Contains(s))
                    throw Invalid(column, $"valor permitido: {string.Join(", ", options)}");
                if (column.Pattern is { } pattern && !Matches(s, pattern))
                    throw Invalid(column, "no tiene el formato esperado");
                break;
            case byte or short or int or long or decimal or double when column.Min is not null || column.Max is not null:
                var n = Convert.ToDecimal(result, CultureInfo.InvariantCulture);
                if (column.Min is { } min && n < min) throw Invalid(column, $"mínimo {min.ToString(CultureInfo.InvariantCulture)}");
                if (column.Max is { } max && n > max) throw Invalid(column, $"máximo {max.ToString(CultureInfo.InvariantCulture)}");
                break;
        }
        return result;
    }

    private static bool Matches(string value, string pattern)
    {
        try { return Overrides.OverrideApplier.MatchesPattern(value, pattern); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    public static object? FromString(ColumnInfo column, string? raw)
    {
        if (raw is null) return CheckNull(column);

        // Un texto vacío en campos no texto se interpreta como nulo (inputs vacíos del formulario).
        if (raw.Length == 0 && column.Type is not (LogicalType.String or LogicalType.Text))
            return CheckNull(column);

        var inv = CultureInfo.InvariantCulture;
        switch (column.Type)
        {
            case LogicalType.String:
            case LogicalType.Text:
                if (column.MaxLength is { } max && raw.Length > max)
                    throw Invalid(column, $"máximo {max} caracteres");
                return raw;

            case LogicalType.Integer:
                return long.TryParse(raw, NumberStyles.Integer, inv, out var l)
                    ? NarrowInteger(column, l)
                    : throw Invalid(column, "debe ser un número entero");

            case LogicalType.Decimal when column.IsLargeNumber:
                // No cabe en decimal de .NET: se valida el formato y viaja como texto (P-023).
                return LargeNumber().IsMatch(raw.Trim())
                    ? raw.Trim()
                    : throw Invalid(column, "debe ser un número (use punto como separador decimal)");

            case LogicalType.Decimal:
                return decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowExponent, inv, out var d)
                    ? IsFloat(column) ? (object)(double)d : d
                    : throw Invalid(column, "debe ser un número");

            case LogicalType.Boolean:
                return raw.Trim().ToLowerInvariant() switch
                {
                    "true" or "1" or "si" or "sí" => true,
                    "false" or "0" or "no" => false,
                    _ => throw Invalid(column, "debe ser verdadero o falso")
                };

            case LogicalType.Date:
                return DateTime.TryParse(raw, inv, DateTimeStyles.None, out var date)
                    ? date.Date
                    : throw Invalid(column, "fecha inválida (use AAAA-MM-DD)");

            case LogicalType.DateTime:
                if (column.HasTimeZone)
                {
                    return DateTimeOffset.TryParse(raw, inv, DateTimeStyles.AssumeUniversal, out var dto)
                        ? dto.ToUniversalTime()
                        : throw Invalid(column, "fecha y hora inválida");
                }
                return DateTime.TryParse(raw, inv, DateTimeStyles.None, out var dt)
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Unspecified)
                    : throw Invalid(column, "fecha y hora inválida");

            case LogicalType.Time:
                return TimeSpan.TryParse(raw, inv, out var ts)
                    ? ts
                    : throw Invalid(column, "hora inválida (use HH:MM)");

            case LogicalType.Uuid:
                return Guid.TryParse(raw, out var g) ? g : throw Invalid(column, "UUID inválido");

            case LogicalType.Json:
                try
                {
                    using var _ = JsonDocument.Parse(raw);
                    return raw;
                }
                catch (JsonException)
                {
                    throw Invalid(column, "JSON inválido");
                }

            case LogicalType.Binary:
                // Archivo en base64 (P-018). Admite el prefijo "data:...;base64," que agregan los navegadores.
                var comma = raw.StartsWith("data:", StringComparison.Ordinal) ? raw.IndexOf(',') : -1;
                try { return Convert.FromBase64String(comma >= 0 ? raw[(comma + 1)..] : raw); }
                catch (FormatException) { throw Invalid(column, "archivo inválido (se esperaba base64)"); }

            case LogicalType.Array:
                // Texto con un arreglo JSON (así llega desde una importación): ["a","b"] o [1,2].
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    return doc.RootElement.ValueKind == JsonValueKind.Array
                        ? FromArray(column, doc.RootElement)
                        : throw Invalid(column, "se esperaba una lista de valores");
                }
                catch (JsonException) { throw Invalid(column, "se esperaba una lista de valores, p. ej. [\"a\", \"b\"]"); }

            case LogicalType.Enum:
                if (column.EnumValues is { Count: > 0 } values && !values.Contains(raw))
                    throw Invalid(column, $"valor permitido: {string.Join(", ", values)}");
                return raw;

            case LogicalType.Spatial:
            {
                // WKT (P-090): la base valida la geometría; aquí solo la forma general, para dar un mensaje claro.
                var wkt = raw.Trim();
                if (wkt.Length == 0) return CheckNull(column);
                if (!Wkt().IsMatch(wkt))
                    throw Invalid(column, "debe estar en formato WKT, p. ej. POINT (-70.66 -33.45)");
                return wkt;
            }

            default:
                throw Invalid(column, "tipo de columna no editable");
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(POINT|LINESTRING|POLYGON|MULTIPOINT|MULTILINESTRING|MULTIPOLYGON|GEOMETRYCOLLECTION|CIRCULARSTRING|COMPOUNDCURVE|CURVEPOLYGON)\s*(Z|M|ZM)?\s*(\(.*\)|EMPTY)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex Wkt();

    /// <summary>
    /// Arreglo (P-018): cada elemento se valida con el tipo del elemento y viaja como texto normalizado;
    /// el dialecto lo convierte al arreglo del motor (en Postgres, CAST a int4[], text[]…).
    /// </summary>
    private static string?[] FromArray(ColumnInfo column, JsonElement array)
    {
        var element = new ColumnInfo
        {
            Name = column.Name,
            NativeType = column.ElementNativeType ?? "text",
            Type = column.ElementType ?? LogicalType.String,
            IsNullable = true
        };
        var result = new List<string?>();
        var i = 0;
        foreach (var item in array.EnumerateArray())
        {
            i++;
            try
            {
                var value = item.ValueKind == JsonValueKind.Null ? null : FromJson(element, item);
                // Una fecha sola va sin hora ("2026-01-31").
                result.Add(value is DateTime d && element.Type == LogicalType.Date
                    ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : Transfer.TransferFormats.ToText(value));
            }
            catch (ValidationException ex)
            {
                throw Invalid(column, $"elemento {i}: {ex.Errors.Values.First()}");
            }
        }
        return result.ToArray();
    }

    private static object? CheckNull(ColumnInfo column) =>
        column.IsNullable || column.HasDefault || column.IsReadOnly
            ? null
            : throw Invalid(column, "es obligatorio");

    private static object NarrowInteger(ColumnInfo column, long value)
    {
        // Se envía el tamaño exacto para que el motor no tenga que convertir (Postgres es estricto).
        var t = column.NativeType.ToLowerInvariant();
        try
        {
            return t switch
            {
                // Cada rama se convierte a object; si no, el switch unifica todo a long.
                "tinyint" => (object)checked((byte)value),
                "smallint" or "int2" => (object)checked((short)value),
                "int" or "int4" or "integer" => (object)checked((int)value),
                _ => (object)value
            };
        }
        catch (OverflowException)
        {
            throw Invalid(column, "número fuera de rango");
        }
    }

    private static bool IsFloat(ColumnInfo column) =>
        column.NativeType.ToLowerInvariant() is "float" or "real" or "float4" or "float8" or "double precision" or "double";

    private static ValidationException Invalid(ColumnInfo column, string message) =>
        new(column.Name, message);
}
