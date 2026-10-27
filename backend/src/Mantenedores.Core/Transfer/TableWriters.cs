using System.Text;
using System.Text.Json;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Mantenedores.Core.Transfer;

/// <summary>Escribe una tabla fila por fila, sin juntar todo en memoria (P-017).</summary>
public interface ITableWriter : IAsyncDisposable
{
    Task WriteHeaderAsync(IReadOnlyList<string> columns, CancellationToken ct);
    Task WriteRowAsync(IReadOnlyList<object?> values, CancellationToken ct);
    Task CompleteAsync(CancellationToken ct);
}

public static class TableWriter
{
    public static ITableWriter Create(TransferFormat format, Stream output, string tableName) => format switch
    {
        TransferFormat.Csv => new CsvTableWriter(output),
        TransferFormat.Json => new JsonTableWriter(output),
        TransferFormat.Xml => new XmlTableWriter(output, tableName),
        _ => new XlsxTableWriter(output, tableName)
    };
}

/// <summary>
/// CSV RFC 4180 en UTF-8 con BOM (Excel lo abre con tildes). Un texto que empieza con = + - @ va con un apóstrofo
/// delante para que una planilla no lo ejecute como fórmula (inyección CSV); al importar se quita.
/// </summary>
public sealed class CsvTableWriter(Stream output) : ITableWriter
{
    private readonly StreamWriter _w = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);

    public Task WriteHeaderAsync(IReadOnlyList<string> columns, CancellationToken ct) =>
        Line(columns.Cast<object?>().ToList(), ct);

    public Task WriteRowAsync(IReadOnlyList<object?> values, CancellationToken ct) => Line(values, ct);

    // Todo asíncrono: ASP.NET no permite escrituras sincrónicas en la respuesta.
    private readonly StringBuilder _line = new();

    private async Task Line(IReadOnlyList<object?> values, CancellationToken ct)
    {
        _line.Clear();
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) _line.Append(',');
            _line.Append(Field(values[i]));
        }
        _line.Append("\r\n");
        await _w.WriteAsync(_line, ct);
    }

    public static bool NeedsFormulaGuard(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    private static string Field(object? value)
    {
        var text = TransferFormats.ToText(value);
        if (text is null) return "";
        if (value is string && NeedsFormulaGuard(text)) text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public async Task CompleteAsync(CancellationToken ct) => await _w.FlushAsync(ct);
    public ValueTask DisposeAsync() => _w.DisposeAsync();
}

/// <summary>JSON: arreglo de objetos con los tipos de JSON (números, booleanos, null).</summary>
public sealed class JsonTableWriter(Stream output) : ITableWriter
{
    private readonly Utf8JsonWriter _w = new(output, new JsonWriterOptions { Indented = true });
    private IReadOnlyList<string> _columns = [];

    public Task WriteHeaderAsync(IReadOnlyList<string> columns, CancellationToken ct)
    {
        _columns = columns;
        _w.WriteStartArray();
        return Task.CompletedTask;
    }

    public async Task WriteRowAsync(IReadOnlyList<object?> values, CancellationToken ct)
    {
        _w.WriteStartObject();
        for (var i = 0; i < _columns.Count; i++)
        {
            _w.WritePropertyName(_columns[i]);
            switch (values[i])
            {
                case null or DBNull: _w.WriteNullValue(); break;
                case bool b: _w.WriteBooleanValue(b); break;
                case var n when TransferFormats.IsNumber(n): _w.WriteRawValue(TransferFormats.ToText(n)!); break;
                case System.Collections.IEnumerable and not string and not byte[]: _w.WriteRawValue(TransferFormats.ToText(values[i])!); break;
                case var v: _w.WriteStringValue(TransferFormats.ToText(v)); break;
            }
        }
        _w.WriteEndObject();
        if (_w.BytesPending > 32 * 1024) await _w.FlushAsync(ct);
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        _w.WriteEndArray();
        await _w.FlushAsync(ct);
    }

    public ValueTask DisposeAsync() => _w.DisposeAsync();
}

/// <summary>
/// XML: <c>&lt;rows table="…"&gt;&lt;row&gt;&lt;field name="col"&gt;valor&lt;/field&gt;…</c>. El nombre va en un atributo
/// porque un nombre de columna puede no ser un nombre XML válido. Un nulo es <c>null="true"</c>.
/// </summary>
public sealed class XmlTableWriter(Stream output, string table) : ITableWriter
{
    private readonly XmlWriter _w = XmlWriter.Create(output, new XmlWriterSettings
    {
        Async = true, Indent = true, Encoding = new UTF8Encoding(false), CloseOutput = false
    });
    private IReadOnlyList<string> _columns = [];

    public async Task WriteHeaderAsync(IReadOnlyList<string> columns, CancellationToken ct)
    {
        _columns = columns;
        await _w.WriteStartDocumentAsync();
        await _w.WriteStartElementAsync(null, "rows", null);
        await _w.WriteAttributeStringAsync(null, "table", null, table);
    }

    public async Task WriteRowAsync(IReadOnlyList<object?> values, CancellationToken ct)
    {
        await _w.WriteStartElementAsync(null, "row", null);
        for (var i = 0; i < _columns.Count; i++)
        {
            await _w.WriteStartElementAsync(null, "field", null);
            await _w.WriteAttributeStringAsync(null, "name", null, _columns[i]);
            if (TransferFormats.ToText(values[i]) is { } text) await _w.WriteStringAsync(text);
            else await _w.WriteAttributeStringAsync(null, "null", null, "true");
            await _w.WriteEndElementAsync();
        }
        await _w.WriteEndElementAsync();
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        await _w.WriteEndElementAsync();
        await _w.WriteEndDocumentAsync();
        await _w.FlushAsync();
    }

    public ValueTask DisposeAsync() => _w.DisposeAsync();
}

/// <summary>
/// Excel (.xlsx) con el SDK de Open XML en modo SAX (fila por fila). El paquete ZIP necesita un stream con posición,
/// así que se arma en un archivo temporal y al final se copia a la salida.
/// Números como números; el resto como texto (fechas en ISO 8601, sin depender de formatos regionales).
/// </summary>
public sealed class XlsxTableWriter : ITableWriter
{
    private readonly Stream _output;
    private readonly FileStream _temp;
    private readonly SpreadsheetDocument _doc;
    private readonly WorkbookPart _workbook;
    private readonly WorksheetPart _sheet;
    private readonly OpenXmlWriter _w;
    private readonly string _sheetName;

    public XlsxTableWriter(Stream output, string table)
    {
        _output = output;
        _temp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
        _doc = SpreadsheetDocument.Create(_temp, SpreadsheetDocumentType.Workbook);
        _workbook = _doc.AddWorkbookPart();
        _sheet = _workbook.AddNewPart<WorksheetPart>();
        _w = OpenXmlWriter.Create(_sheet);
        _sheetName = SheetName(table);
    }

    /// <summary>Nombre de hoja válido: hasta 31 caracteres y sin []:*?/\.</summary>
    private static string SheetName(string table)
    {
        var clean = new string(table.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray());
        return clean.Length == 0 ? "Datos" : clean[..Math.Min(31, clean.Length)];
    }

    public Task WriteHeaderAsync(IReadOnlyList<string> columns, CancellationToken ct)
    {
        _w.WriteStartElement(new Worksheet());
        _w.WriteStartElement(new SheetData());
        WriteRow(columns.Cast<object?>().ToList());
        return Task.CompletedTask;
    }

    public Task WriteRowAsync(IReadOnlyList<object?> values, CancellationToken ct)
    {
        WriteRow(values);
        return Task.CompletedTask;
    }

    private void WriteRow(IReadOnlyList<object?> values)
    {
        _w.WriteStartElement(new Row());
        foreach (var v in values)
        {
            var text = TransferFormats.ToText(v);
            if (text is null) _w.WriteElement(new Cell());
            else if (TransferFormats.IsNumber(v) && text.Length <= 15)
                _w.WriteElement(new Cell { DataType = CellValues.Number, CellValue = new CellValue(text) });
            else if (v is bool b)
                _w.WriteElement(new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(b ? "1" : "0") });
            else
                // Los números de más de 15 dígitos van como texto: Excel los redondearía.
                _w.WriteElement(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve })
                });
        }
        _w.WriteEndElement();
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        _w.WriteEndElement(); // SheetData
        _w.WriteEndElement(); // Worksheet
        _w.Close();
        _workbook.Workbook = new Workbook(new Sheets(new Sheet { Id = _workbook.GetIdOfPart(_sheet), SheetId = 1, Name = _sheetName }));
        _doc.Dispose();
        _temp.Position = 0;
        await _temp.CopyToAsync(_output, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _w.Dispose();
        _doc.Dispose();
        await _temp.DisposeAsync();
    }
}
