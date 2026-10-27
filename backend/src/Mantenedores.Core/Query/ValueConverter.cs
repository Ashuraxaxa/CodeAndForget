using System.Globalization;
using System.Text.Json;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Query;

/// <summary>Convierte valores de la API (JSON o texto de la URL) al tipo CLR que espera cada columna.</summary>
public static class ValueConverter
{
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
        _ => throw Invalid(column, "formato no soportado")
    };

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

            case LogicalType.Enum:
                if (column.EnumValues is { Count: > 0 } values && !values.Contains(raw))
                    throw Invalid(column, $"valor permitido: {string.Join(", ", values)}");
                return raw;

            default:
                throw Invalid(column, "tipo de columna no editable");
        }
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
        column.NativeType.ToLowerInvariant() is "float" or "real" or "float4" or "float8" or "double precision";

    private static ValidationException Invalid(ColumnInfo column, string message) =>
        new(column.Name, message);
}
