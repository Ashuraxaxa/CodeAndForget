using System.Globalization;

namespace Mantenedores.Core.Transfer;

/// <summary>Formatos de exportación e importación (P-017).</summary>
public enum TransferFormat
{
    Csv,
    Xlsx,
    Json,
    Xml
}

public static class TransferFormats
{
    public static TransferFormat Parse(string? text) => (text ?? "").Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "csv" or "txt" => TransferFormat.Csv,
        "xlsx" or "excel" => TransferFormat.Xlsx,
        "json" => TransferFormat.Json,
        "xml" => TransferFormat.Xml,
        _ => throw new Errors.ValidationException("format", "formato no soportado: use csv, xlsx, json o xml")
    };

    public static TransferFormat FromFileName(string fileName) => Parse(Path.GetExtension(fileName));

    public static string ContentType(TransferFormat f) => f switch
    {
        TransferFormat.Csv => "text/csv; charset=utf-8",
        TransferFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        TransferFormat.Json => "application/json; charset=utf-8",
        _ => "application/xml; charset=utf-8"
    };

    public static string Extension(TransferFormat f) => f switch
    {
        TransferFormat.Csv => "csv",
        TransferFormat.Xlsx => "xlsx",
        TransferFormat.Json => "json",
        _ => "xml"
    };

    /// <summary>Un valor como texto estable, igual en todos los motores: fechas ISO 8601 y números con punto.</summary>
    public static string? ToText(object? value) => value switch
    {
        null or DBNull => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        DateTime { Kind: DateTimeKind.Utc } dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        Guid g => g.ToString("D"),
        byte[] bytes => Convert.ToBase64String(bytes),
        // Arreglos (P-018): como arreglo JSON, que la importación vuelve a leer.
        System.Collections.IEnumerable items => System.Text.Json.JsonSerializer.Serialize(items.Cast<object?>().Select(ToText)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static bool IsNumber(object? value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or double or float;
}
