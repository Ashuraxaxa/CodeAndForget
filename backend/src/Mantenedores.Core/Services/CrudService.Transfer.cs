using System.Data.Common;
using System.Text.Json;
using Dapper;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Transfer;

namespace Mantenedores.Core.Services;

/// <summary>Una exportación lista para escribir: ya se validó el formato y el tope de filas.</summary>
public sealed record ExportPlan(TransferFormat Format, string FileName, long Rows, Func<Stream, CancellationToken, Task> WriteAsync);

/// <summary>Error de una fila al importar. <c>Line</c> es la línea del archivo (o el número de elemento en JSON/XML).</summary>
public sealed record ImportError(int Line, string? Column, string Message);

/// <summary>Columna de destino que se le ofrece al usuario para asociar.</summary>
public sealed record ImportTarget(string Name, string? Label, LogicalType Type, bool Required);

/// <summary>
/// Resultado de la vista previa o de la importación. <c>Mapping</c>: encabezado del archivo → columna (null = se ignora).
/// <c>Sample</c>: primeras filas ya asociadas. <c>Imported</c> &gt; 0 solo si se grabó.
/// </summary>
public sealed record ImportResult(
    IReadOnlyList<string> Headers,
    IReadOnlyDictionary<string, string?> Mapping,
    IReadOnlyList<ImportTarget> Targets,
    int TotalRows,
    int ValidRows,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Sample,
    int Imported);

public sealed partial class CrudService
{
    private const int MaxReportedErrors = 200;
    private const int SampleRows = 20;

    // ---------- Exportar ----------

    /// <summary>
    /// Prepara la exportación del listado filtrado (mismos filtros, búsqueda y orden; sin paginar).
    /// Cuenta antes de escribir: si pasa el tope, falla aquí, cuando todavía se puede devolver un error.
    /// </summary>
    public async Task<ExportPlan> PrepareExportAsync(
        string conn, string schema, string table, ListRequest request, TransferFormat format, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        var max = Math.Max(1, options.ExportMaxRows);
        var (data, count) = new QueryBuilder(ctx.Provider.Dialect)
            .BuildList(ctx.Schema, info, request with { Page = 1, PageSize = max }, max);

        long total;
        await using (var db = await OpenAsync(ctx, ct))
            total = await db.ExecuteScalarAsync<long>(Command(count, ct));
        if (total > max)
            throw new ValidationException("export", $"hay {total} filas y el máximo por exportación es {max} (Crud:ExportMaxRows): filtre antes de exportar");

        // Columnas: las visibles, y después de cada FK su texto ("categoria_id__label").
        var labels = data.Renames?.Values.ToHashSet() ?? [];
        var columns = new List<string>();
        foreach (var c in info.Columns.Where(c => c.IsSelectable && !c.Hidden))
        {
            columns.Add(c.Name);
            if (labels.Contains(c.Name + QueryBuilder.LabelSuffix)) columns.Add(c.Name + QueryBuilder.LabelSuffix);
        }

        async Task Write(Stream output, CancellationToken token)
        {
            await using var writer = TableWriter.Create(format, output, info.Name);
            await writer.WriteHeaderAsync(columns, token);
            await using var db = await OpenAsync(ctx, token);
            var rows = db.QueryUnbufferedAsync(data.Sql, new DynamicParameters(data.Parameters), commandTimeout: options.CommandTimeoutSeconds);
            await foreach (var r in rows.WithCancellation(token))
            {
                IDictionary<string, object?> row = Normalize(info, Rename(ToRow((object)r), data.Renames));
                await writer.WriteRowAsync(columns.Select(c => row.TryGetValue(c, out var v) ? v : null).ToList(), token);
            }
            await writer.CompleteAsync(token);
        }

        return new ExportPlan(format, $"{info.Name}.{TransferFormats.Extension(format)}", total, Write);
    }

    // ---------- Importar ----------

    /// <summary>Lee el archivo, asocia columnas y valida cada fila sin grabar nada.</summary>
    public Task<ImportResult> PreviewImportAsync(
        string conn, string schema, string table, Stream file, TransferFormat format,
        IReadOnlyDictionary<string, string?>? mapping, CancellationToken ct) =>
        ImportCoreAsync(conn, schema, table, file, format, mapping, commit: false, ct);

    /// <summary>Valida todo y, si no hay errores, inserta todas las filas en una transacción: o entran todas o ninguna.</summary>
    public Task<ImportResult> ImportAsync(
        string conn, string schema, string table, Stream file, TransferFormat format,
        IReadOnlyDictionary<string, string?>? mapping, CancellationToken ct) =>
        ImportCoreAsync(conn, schema, table, file, format, mapping, commit: true, ct);

    private async Task<ImportResult> ImportCoreAsync(
        string conn, string schema, string table, Stream file, TransferFormat format,
        IReadOnlyDictionary<string, string?>? requested, bool commit, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        if (!info.IsEditable) throw new OperationNotAllowedException($"{info.FullName} es de solo lectura: no se puede importar.");

        var data = await TableReader.ReadAsync(file, format, Math.Max(1, options.ImportMaxRows), ct);
        var writable = info.Columns.Where(c => c.IsSelectable && !c.Hidden && !c.IsReadOnly).ToList();
        var targets = writable.Select(c => new ImportTarget(c.Name, c.Label, c.Type, c.IsRequiredOnInsert)).ToList();
        var mapping = Map(data.Headers, writable, requested);

        var errors = new List<ImportError>();
        foreach (var t in targets.Where(t => t.Required && !mapping.Values.Contains(t.Name, StringComparer.OrdinalIgnoreCase)))
            errors.Add(new ImportError(0, t.Name, "es obligatoria y ninguna columna del archivo está asociada a ella"));

        // Validar y convertir cada fila con las mismas reglas que un alta desde el formulario.
        var parsed = new List<(int Line, Dictionary<string, object?> Values)>();
        for (var i = 0; i < data.Rows.Count; i++)
        {
            var line = data.FirstDataLine + i;
            var body = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (header, column) in mapping)
            {
                if (column is null) continue;
                var col = info.FindColumn(column)!;
                var value = data.Rows[i].GetValueOrDefault(header);
                if (format == TransferFormat.Xlsx && col.Type is LogicalType.Date or LogicalType.DateTime)
                    value = TableReader.ExcelDate(value, col.Type == LogicalType.DateTime);
                body[col.Name] = JsonSerializer.SerializeToElement(value);
            }
            try
            {
                // Una obligatoria sin asociar ya es un error general (línea 0); una asociada y vacía la rechaza ParseValues.
                parsed.Add((line, ParseValues(info, body)));
            }
            catch (ValidationException ex)
            {
                foreach (var (col, msg) in ex.Errors) errors.Add(new ImportError(line, col, msg));
            }
        }

        var invalidLines = errors.Where(e => e.Line > 0).Select(e => e.Line).ToHashSet();
        var validRows = data.Rows.Count - invalidLines.Count;
        var sample = data.Rows.Take(SampleRows)
            .Select(r => (IReadOnlyDictionary<string, string?>)mapping.Where(m => m.Value is not null)
                .ToDictionary(m => m.Value!, m => r.GetValueOrDefault(m.Key)))
            .ToList();

        ImportResult Result(int imported, IReadOnlyList<ImportError> errs) => new(
            data.Headers, mapping, targets, data.Rows.Count, validRows, errs.Take(MaxReportedErrors).ToList(), sample, imported);

        if (!commit || errors.Count > 0 || parsed.Count == 0) return Result(0, errors);

        // Todas en una transacción: si la base rechaza una fila, no queda ninguna.
        var qb = new QueryBuilder(ctx.Provider.Dialect);
        await using var db = await OpenAsync(ctx, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var (line, values) in parsed)
        {
            var insert = qb.BuildInsert(info, values);
            try
            {
                await Translated(ctx, info, DbOperation.Insert, () => db.ExecuteAsync(Command(insert.Query, ct, tx)));
            }
            catch (ConstraintViolationException ex)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return Result(0, [new ImportError(line, ex.Errors.Keys.FirstOrDefault(), ex.Message)]);
            }
            catch (DbException ex)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return Result(0, [new ImportError(line, null, ex.Message)]);
            }
        }
        await tx.CommitAsync(ct);
        return Result(parsed.Count, []);
    }

    /// <summary>
    /// Asociación encabezado → columna. Lo pedido por el usuario manda; lo demás se asocia solo por nombre o etiqueta,
    /// sin distinguir mayúsculas. Los textos de FK ("x__label") y las columnas de solo lectura se ignoran.
    /// </summary>
    private static Dictionary<string, string?> Map(
        IReadOnlyList<string> headers, IReadOnlyList<ColumnInfo> writable, IReadOnlyDictionary<string, string?>? requested)
    {
        // "Código", "codigo" y "CODIGO_" valen lo mismo: sin tildes, espacios ni guiones bajos.
        static string Norm(string s) => new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                        && c is not ('_' or ' ' or '-'))
            .ToArray()).ToLowerInvariant();
        var map = new Dictionary<string, string?>();
        foreach (var h in headers)
        {
            if (requested is not null && requested.TryGetValue(h, out var chosen))
            {
                if (chosen is null || chosen.Length == 0) { map[h] = null; continue; }
                map[h] = writable.FirstOrDefault(c => string.Equals(c.Name, chosen, StringComparison.OrdinalIgnoreCase))?.Name
                         ?? throw new ValidationException(h, $"la columna '{chosen}' no existe o no se puede escribir");
                continue;
            }
            if (h.EndsWith(QueryBuilder.LabelSuffix, StringComparison.Ordinal)) { map[h] = null; continue; }
            map[h] = writable.FirstOrDefault(c => string.Equals(c.Name, h, StringComparison.OrdinalIgnoreCase))?.Name
                     ?? writable.FirstOrDefault(c => c.Label is { } l && Norm(l) == Norm(h))?.Name
                     ?? writable.FirstOrDefault(c => Norm(c.Name) == Norm(h))?.Name;
        }
        // Dos encabezados a la misma columna: vale el primero.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            if (map[h] is { } c && !used.Add(c)) map[h] = null;
        return map;
    }
}
