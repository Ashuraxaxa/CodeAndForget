using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Transfer;

/// <summary>
/// Archivo leído para importar: encabezados (en orden) y filas como texto; null = vacío explícito (JSON null, XML null="true").
/// <c>FirstDataLine</c>: número de línea de la primera fila de datos, para informar errores como los ve el usuario.
/// </summary>
public sealed record ImportFile(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows, int FirstDataLine);

public static class TableReader
{
    public static async Task<ImportFile> ReadAsync(Stream input, TransferFormat format, int maxRows, CancellationToken ct)
    {
        try
        {
            var file = format switch
            {
                TransferFormat.Csv => await ReadCsvAsync(input, maxRows, ct),
                TransferFormat.Xlsx => await ReadXlsxAsync(input, maxRows, ct),
                TransferFormat.Json => await ReadJsonAsync(input, maxRows, ct),
                _ => await ReadXmlAsync(input, maxRows, ct)
            };
            if (file.Headers.Count == 0) throw new ValidationException("file", "el archivo no tiene columnas");
            return file;
        }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidDataException or OpenXmlPackageException or FormatException)
        {
            throw new ValidationException("file", $"no se pudo leer el archivo como {TransferFormats.Extension(format)}: {ex.Message}");
        }
    }

    private static void CheckLimit(int count, int maxRows)
    {
        if (count > maxRows) throw new ValidationException("file", $"el archivo tiene más de {maxRows} filas (límite Crud:ImportMaxRows)");
    }

    // ---------- CSV ----------

    /// <summary>CSV RFC 4180; el separador (coma, punto y coma o tabulación) se deduce de la primera línea.</summary>
    private static async Task<ImportFile> ReadCsvAsync(Stream input, int maxRows, CancellationToken ct)
    {
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        var firstLine = text.Split('\n', 2)[0];
        var sep = new[] { ',', ';', '\t' }.MaxBy(c => CountOutsideQuotes(firstLine, c));

        var records = ParseCsv(text, sep);
        if (records.Count == 0) return new([], [], 2);
        var headers = records[0].Select(h => h.Trim()).ToList();
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var r in records.Skip(1))
        {
            if (r.All(v => v.Length == 0)) continue; // líneas vacías
            CheckLimit(rows.Count + 1, maxRows);
            var row = new Dictionary<string, string?>();
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = i < r.Count ? Unguard(r[i]) : "";
            rows.Add(row);
        }
        return new(headers, rows, 2);
    }

    /// <summary>Quita el apóstrofo que el exportador pone delante de un texto que parece fórmula.</summary>
    private static string Unguard(string v) =>
        v.Length > 1 && v[0] == '\'' && CsvTableWriter.NeedsFormulaGuard(v[1..]) ? v[1..] : v;

    private static int CountOutsideQuotes(string line, char c)
    {
        var n = 0;
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == c && !quoted) n++;
        }
        return n;
    }

    public static List<List<string>> ParseCsv(string text, char sep)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        return records;
    }

    // ---------- Excel ----------

    /// <summary>Primera hoja; la primera fila son los encabezados. Las celdas se leen como texto (números tal cual).</summary>
    private static async Task<ImportFile> ReadXlsxAsync(Stream input, int maxRows, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        using var doc = SpreadsheetDocument.Open(buffer, false);
        var wb = doc.WorkbookPart ?? throw new InvalidDataException("no tiene libro");
        var sheet = wb.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new InvalidDataException("no tiene hojas");
        var part = (WorksheetPart)wb.GetPartById(sheet.Id!);
        var shared = wb.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];

        string? Value(Cell c)
        {
            var raw = c.CellValue?.Text;
            if (c.DataType?.Value == CellValues.SharedString)
                return int.TryParse(raw, out var idx) && idx < shared.Count ? shared[idx] : "";
            if (c.DataType?.Value == CellValues.InlineString) return c.InlineString?.InnerText ?? "";
            if (c.DataType?.Value == CellValues.Boolean) return raw == "1" ? "true" : "false";
            return raw ?? "";
        }

        var rows = part.Worksheet.Descendants<Row>().ToList();
        if (rows.Count == 0) return new([], [], 2);

        // Las celdas vacías no siempre están: la columna sale de la referencia (A1, B1…).
        static int ColumnIndex(string? reference, int fallback)
        {
            if (string.IsNullOrEmpty(reference)) return fallback;
            var n = 0;
            foreach (var ch in reference.TakeWhile(char.IsLetter)) n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            return n - 1;
        }
        Dictionary<int, string?> Cells(Row r)
        {
            var d = new Dictionary<int, string?>();
            var i = 0;
            foreach (var c in r.Elements<Cell>()) { i = ColumnIndex(c.CellReference?.Value, i); d[i] = Value(c); i++; }
            return d;
        }

        var head = Cells(rows[0]);
        var headers = head.OrderBy(kv => kv.Key).Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => (kv.Key, Name: kv.Value!.Trim())).ToList();
        var result = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var r in rows.Skip(1))
        {
            var cells = Cells(r);
            if (cells.Values.All(string.IsNullOrEmpty)) continue;
            CheckLimit(result.Count + 1, maxRows);
            result.Add(headers.ToDictionary(h => h.Name, h => (string?)(cells.GetValueOrDefault(h.Key) ?? "")));
        }
        return new(headers.Select(h => h.Name).ToList(), result, 2);
    }

    // ---------- JSON ----------

    /// <summary>Arreglo de objetos. Los números se toman tal cual; objetos y arreglos, como su JSON (para columnas json).</summary>
    private static async Task<ImportFile> ReadJsonAsync(Stream input, int maxRows, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(input, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("se esperaba un arreglo de objetos");
        var headers = new List<string>();
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new FormatException("cada elemento debe ser un objeto");
            CheckLimit(rows.Count + 1, maxRows);
            var row = new Dictionary<string, string?>();
            foreach (var p in item.EnumerateObject())
            {
                if (!headers.Contains(p.Name)) headers.Add(p.Name);
                row[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.GetRawText()
                };
            }
            rows.Add(row);
        }
        return new(headers, rows, 1);
    }

    // ---------- XML ----------

    /// <summary>
    /// Cada hijo de la raíz es una fila. Sus hijos son los campos: <c>&lt;field name="col"&gt;</c> (lo que exporta la app)
    /// o un elemento con el nombre de la columna (<c>&lt;nombre&gt;Agua&lt;/nombre&gt;</c>). <c>null="true"</c> = nulo.
    /// </summary>
    private static async Task<ImportFile> ReadXmlAsync(Stream input, int maxRows, CancellationToken ct)
    {
        // Se copia antes a memoria: XmlDocument lee de forma sincrónica y ASP.NET no lo permite sobre la solicitud.
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(buffer, settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);

        var headers = new List<string>();
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var rowNode in doc.DocumentElement?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            CheckLimit(rows.Count + 1, maxRows);
            var row = new Dictionary<string, string?>();
            foreach (var f in rowNode.ChildNodes.OfType<XmlElement>())
            {
                var name = f.LocalName == "field" && f.HasAttribute("name") ? f.GetAttribute("name") : f.LocalName;
                if (!headers.Contains(name)) headers.Add(name);
                row[name] = f.GetAttribute("null") == "true" ? null : f.InnerText;
            }
            rows.Add(row);
        }
        return new(headers, rows, 1);
    }

    /// <summary>Excel guarda las fechas como número de días (OADate): si una columna de fecha trae un número, se convierte.</summary>
    public static string? ExcelDate(string? value, bool withTime)
    {
        if (value is null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) || oa < 1 || oa > 2958465)
            return value;
        var dt = DateTime.FromOADate(oa);
        return withTime
            ? dt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
