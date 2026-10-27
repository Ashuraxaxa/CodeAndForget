using System.Text;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Transfer;

namespace Mantenedores.Tests;

/// <summary>Exportar e importar (P-017): lo que se escribe en cada formato se vuelve a leer igual.</summary>
public class TransferTests
{
    private static readonly string[] Columns = ["id", "nombre", "precio", "activo", "alta", "nota"];

    private static readonly object?[][] Rows =
    [
        [1, "Agua \"mineral\", 500ml", 690.5m, true, new DateOnly(2026, 1, 31), null],
        [2, "Línea 1\nLínea 2", -3m, false, new DateOnly(2026, 2, 1), "=SUMA(A1:A2)"],
        [3, "  con espacios  ", 1234567890123456789m, null, null, "@usuario"]
    ];

    private static async Task<ImportFile> RoundTrip(TransferFormat format)
    {
        var ms = new MemoryStream();
        await using (var w = TableWriter.Create(format, ms, "producto"))
        {
            await w.WriteHeaderAsync(Columns, default);
            foreach (var r in Rows) await w.WriteRowAsync(r, default);
            await w.CompleteAsync(default);
        }
        ms.Position = 0;
        return await TableReader.ReadAsync(ms, format, 100, default);
    }

    [Theory]
    [InlineData(TransferFormat.Csv)]
    [InlineData(TransferFormat.Xlsx)]
    [InlineData(TransferFormat.Json)]
    [InlineData(TransferFormat.Xml)]
    public async Task Lo_exportado_se_vuelve_a_leer_igual(TransferFormat format)
    {
        var file = await RoundTrip(format);
        Assert.Equal(Columns, file.Headers);
        Assert.Equal(3, file.Rows.Count);

        Assert.Equal("1", file.Rows[0]["id"]);
        Assert.Equal("Agua \"mineral\", 500ml", file.Rows[0]["nombre"]);
        Assert.Equal("690.5", file.Rows[0]["precio"]);
        Assert.Equal("true", file.Rows[0]["activo"]);
        Assert.Equal("2026-01-31", file.Rows[0]["alta"]);
        Assert.Equal("Línea 1\nLínea 2", file.Rows[1]["nombre"]);
        Assert.Equal("=SUMA(A1:A2)", file.Rows[1]["nota"]); // el apóstrofo del CSV se quita al leer
        Assert.Equal("  con espacios  ", file.Rows[2]["nombre"]);
        Assert.Equal("1234567890123456789", file.Rows[2]["precio"]); // sin redondear (en Excel va como texto)

        // Vacío: CSV y Excel no distinguen nulo de vacío; JSON y XML sí.
        var expectedNull = format is TransferFormat.Json or TransferFormat.Xml ? null : "";
        Assert.Equal(expectedNull, file.Rows[0]["nota"]);
        Assert.Equal(format is TransferFormat.Csv or TransferFormat.Xlsx ? 2 : 1, file.FirstDataLine);
    }

    [Fact]
    public async Task El_csv_protege_contra_formulas_y_usa_bom_utf8()
    {
        var ms = new MemoryStream();
        await using (var w = new CsvTableWriter(ms))
        {
            await w.WriteHeaderAsync(["a", "b"], default);
            await w.WriteRowAsync(["=1+1", -5], default);
            await w.CompleteAsync(default);
        }
        var bytes = ms.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("a,b\r\n'=1+1,-5\r\n", Encoding.UTF8.GetString(bytes[3..])); // el número negativo no lleva apóstrofo
    }

    [Theory]
    [InlineData("nombre;precio\r\nAgua;690\r\n", ';')]
    [InlineData("nombre\tprecio\nAgua\t690\n", '\t')]
    public async Task El_csv_deduce_el_separador(string csv, char _)
    {
        var file = await TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), TransferFormat.Csv, 10, default);
        Assert.Equal(["nombre", "precio"], file.Headers);
        Assert.Equal("690", Assert.Single(file.Rows)["precio"]);
    }

    [Fact]
    public async Task Xml_acepta_elementos_con_el_nombre_de_la_columna()
    {
        const string xml = "<productos><producto><nombre>Agua</nombre><precio>690</precio></producto></productos>";
        var file = await TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)), TransferFormat.Xml, 10, default);
        Assert.Equal("Agua", Assert.Single(file.Rows)["nombre"]);
    }

    [Fact]
    public async Task Xml_no_resuelve_entidades_externas()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r><row><a>&x;</a></row></r>";
        await Assert.ThrowsAsync<ValidationException>(() =>
            TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)), TransferFormat.Xml, 10, default));
    }

    [Fact]
    public async Task Respeta_el_maximo_de_filas_y_explica_los_archivos_invalidos()
    {
        var csv = "a\n1\n2\n3\n";
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), TransferFormat.Csv, 2, default));
        Assert.Contains("más de 2 filas", ex.Errors["file"]);

        ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}")), TransferFormat.Json, 10, default));
        Assert.Contains("arreglo", ex.Errors["file"]);

        await Assert.ThrowsAsync<ValidationException>(() =>
            TableReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("no es excel")), TransferFormat.Xlsx, 10, default));
    }

    [Fact]
    public void Las_fechas_de_excel_se_convierten()
    {
        Assert.Equal("2026-01-31", TableReader.ExcelDate("46053", withTime: false));
        Assert.Equal("2026-01-31T12:00:00", TableReader.ExcelDate("46053.5", withTime: true));
        Assert.Equal("2026-01-31", TableReader.ExcelDate("2026-01-31", withTime: false)); // ya es texto
    }

    [Fact]
    public void Formatos_por_nombre_y_extension()
    {
        Assert.Equal(TransferFormat.Xlsx, TransferFormats.FromFileName("productos.XLSX"));
        Assert.Equal(TransferFormat.Json, TransferFormats.Parse("json"));
        Assert.Throws<ValidationException>(() => TransferFormats.Parse("pdf"));
    }
}
