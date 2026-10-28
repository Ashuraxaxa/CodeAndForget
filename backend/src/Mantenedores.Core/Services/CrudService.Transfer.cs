using System.Data.Common;
using System.Text.Json;
using Dapper;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;
using Mantenedores.Core.Transfer;

namespace Mantenedores.Core.Services;

/// <summary>Una exportación lista para escribir: ya se validó el formato y el tope de filas.</summary>
public sealed record ExportPlan(TransferFormat Format, string FileName, long Rows, Func<Stream, CancellationToken, Task> WriteAsync);

/// <summary>Error de una fila al importar. <c>Line</c> es la línea del archivo (o el número de elemento en JSON/XML).</summary>
public sealed record ImportError(int Line, string? Column, string Message);

/// <summary>
/// Columna de destino que se le ofrece al usuario para asociar. <c>IsKey</c>: identifica la fila a actualizar
/// (modo <see cref="ImportMode.Upsert"/>); si la genera la base, solo sirve para eso.
/// </summary>
public sealed record ImportTarget(string Name, string? Label, LogicalType Type, bool Required, bool IsKey = false);

/// <summary>Insert: todas las filas son altas. Upsert: las que traen una clave existente se actualizan (P-088).</summary>
public enum ImportMode { Insert, Upsert }

/// <summary>
/// Resultado de la vista previa o de la importación. <c>Mapping</c>: encabezado del archivo → columna (null = se ignora).
/// <c>Sample</c>: primeras filas ya asociadas. <c>Imported</c> &gt; 0 solo si se grabó.
/// <c>Created</c> y <c>Updated</c>: en la vista previa, lo que se haría; al importar, lo que se hizo.
/// </summary>
public sealed record ImportResult(
    IReadOnlyList<string> Headers,
    IReadOnlyDictionary<string, string?> Mapping,
    IReadOnlyList<ImportTarget> Targets,
    int TotalRows,
    int ValidRows,
    IReadOnlyList<ImportError> Errors,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Sample,
    int Imported,
    int Created = 0,
    int Updated = 0);

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
        Require(info, DataOperation.Export, "exportar");
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
        IReadOnlyDictionary<string, string?>? mapping, CancellationToken ct, ImportMode mode = ImportMode.Insert) =>
        ImportCoreAsync(conn, schema, table, file, format, mapping, mode, commit: false, ct);

    /// <summary>Valida todo y, si no hay errores, inserta todas las filas en una transacción: o entran todas o ninguna.</summary>
    public Task<ImportResult> ImportAsync(
        string conn, string schema, string table, Stream file, TransferFormat format,
        IReadOnlyDictionary<string, string?>? mapping, CancellationToken ct, ImportMode mode = ImportMode.Insert) =>
        ImportCoreAsync(conn, schema, table, file, format, mapping, mode, commit: true, ct);

    private async Task<ImportResult> ImportCoreAsync(
        string conn, string schema, string table, Stream file, TransferFormat format,
        IReadOnlyDictionary<string, string?>? requested, ImportMode mode, bool commit, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        Require(info, DataOperation.Import, "importar en");
        Require(info, DataOperation.Create, "crear registros en");
        if (!info.IsEditable) throw new OperationNotAllowedException($"{info.FullName} es de solo lectura: no se puede importar.");
        var upsert = mode == ImportMode.Upsert;
        if (upsert) Require(info, DataOperation.Update, "editar registros de");

        var data = await TableReader.ReadAsync(file, format, Math.Max(1, options.ImportMaxRows), ct);
        var writable = info.Columns.Where(c => c.IsSelectable && !c.Hidden && !c.IsReadOnly).ToList();
        // Al actualizar, la clave también se asocia aunque la genere la base (identity): sirve para encontrar la fila.
        var keyColumns = upsert ? info.PrimaryKey.Select(k => info.FindColumn(k)!).ToList() : [];
        var mappable = writable.Concat(keyColumns.Where(k => !writable.Contains(k))).ToList();
        var targets = mappable.Select(c => new ImportTarget(c.Name, c.Label, c.Type,
            !upsert && c.IsRequiredOnInsert, keyColumns.Contains(c))).ToList();
        var mapping = Map(data.Headers, mappable, requested);
        bool Mapped(string column) => mapping.Values.Contains(column, StringComparer.OrdinalIgnoreCase);

        var errors = new List<ImportError>();
        foreach (var t in targets.Where(t => t.Required && !Mapped(t.Name)))
            errors.Add(new ImportError(0, t.Name, "es obligatoria y ninguna columna del archivo está asociada a ella"));
        foreach (var k in keyColumns.Where(k => !Mapped(k.Name)))
            errors.Add(new ImportError(0, k.Name, "es parte de la clave: asóciela para poder actualizar"));

        // Validar y convertir cada fila con las mismas reglas que un alta desde el formulario.
        var parsed = new List<ImportRow>();
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
                var values = ParseValues(info, body, MaxFileBytes);
                Dictionary<string, object?>? key = null;
                if (upsert)
                {
                    key = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var k in keyColumns)
                        key[k.Name] = body.TryGetValue(k.Name, out var v) ? ValueConverter.FromJson(k, v) : null;
                    if (key.Values.Any(v => v is null)) key = null; // sin clave completa: es un alta
                }
                parsed.Add(new ImportRow(line, values, key));
            }
            catch (ValidationException ex)
            {
                foreach (var (col, msg) in ex.Errors) errors.Add(new ImportError(line, col, msg));
            }
        }

        // Actualizar: qué claves ya existen. Las que no, son altas (si la clave la genera la base, no se puede elegir).
        if (upsert && parsed.Any(r => r.Key is not null) && errors.All(e => e.Line > 0))
        {
            await using var check = await OpenAsync(ctx, ct);
            var qb0 = new QueryBuilder(ctx.Provider.Dialect);
            var existing = new HashSet<string>();
            foreach (var chunk in parsed.Where(r => r.Key is not null).Chunk(100))
            {
                var rows = await Translated(ctx, info, DbOperation.Read,
                    () => check.QueryAsync(Command(qb0.BuildExistingKeys(info, chunk.Select(r => (IReadOnlyDictionary<string, object?>)r.Key!).ToList()), ct)));
                foreach (IDictionary<string, object?> row in rows) existing.Add(KeyText(info, row));
            }
            var generated = keyColumns.Any(k => k.IsReadOnly);
            for (var n = 0; n < parsed.Count; n++)
            {
                var r = parsed[n];
                if (r.Key is null) continue;
                if (existing.Contains(KeyText(info, r.Key))) parsed[n] = r with { Exists = true };
                else if (generated)
                    errors.Add(new ImportError(r.Line, keyColumns[0].Name,
                        "no existe un registro con esa clave; la genera la base: deje la clave vacía para crear uno nuevo"));
            }
        }
        // Las altas necesitan las obligatorias aunque en modo actualizar no se hayan exigido de entrada.
        if (upsert)
            foreach (var r in parsed.Where(r => !r.Exists))
                foreach (var c in writable.Where(c => c.IsRequiredOnInsert && !r.Values.ContainsKey(c.Name)))
                    errors.Add(new ImportError(r.Line, c.Name, "es obligatoria para crear un registro nuevo"));

        var invalidLines = errors.Where(e => e.Line > 0).Select(e => e.Line).ToHashSet();
        var validRows = data.Rows.Count - invalidLines.Count;
        var sample = data.Rows.Take(SampleRows)
            .Select(r => (IReadOnlyDictionary<string, string?>)mapping.Where(m => m.Value is not null)
                .ToDictionary(m => m.Value!, m => r.GetValueOrDefault(m.Key)))
            .ToList();
        var toUpdate = parsed.Count(r => r.Exists);
        var toCreate = parsed.Count - toUpdate;

        ImportResult Result(int imported, IReadOnlyList<ImportError> errs, int created, int updated) => new(
            data.Headers, mapping, targets, data.Rows.Count, validRows, errs.Take(MaxReportedErrors).ToList(), sample, imported, created, updated);

        if (!commit || errors.Count > 0 || parsed.Count == 0)
            return Result(0, errors, errors.Count == 0 ? toCreate : 0, errors.Count == 0 ? toUpdate : 0);

        // Para la auditoría y los avisos: cómo estaban las filas que se van a actualizar.
        var befores = new Dictionary<int, IDictionary<string, object?>>();
        if (TracksChanges(info))
            foreach (var r in parsed.Where(r => r.Exists))
                befores[r.Line] = await GetByKeyAsync(ctx, info, r.Key!, ct);

        // Todas en una transacción: si la base rechaza una fila, no queda ninguna.
        var qb = new QueryBuilder(ctx.Provider.Dialect);
        await using (var db = await OpenAsync(ctx, ct))
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            foreach (var r in parsed)
            {
                try
                {
                    if (r.Exists)
                    {
                        // Solo lo que trae el archivo, sin la clave (la clave identifica la fila).
                        var values = r.Values.Where(kv => !keyColumns.Any(k => string.Equals(k.Name, kv.Key, StringComparison.OrdinalIgnoreCase)))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                        if (values.Count == 0) continue;
                        var update = qb.BuildUpdate(info, r.Key!, values);
                        await Translated(ctx, info, DbOperation.Update, () => db.ExecuteAsync(Command(update, ct, tx)));
                    }
                    else
                    {
                        var insert = qb.BuildInsert(info, r.Values);
                        await Translated(ctx, info, DbOperation.Insert, () => db.ExecuteAsync(Command(insert.Query, ct, tx)));
                    }
                }
                catch (ConstraintViolationException ex)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    return Result(0, [new ImportError(r.Line, ex.Errors.Keys.FirstOrDefault(), ex.Message)], 0, 0);
                }
                catch (DbException ex)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    return Result(0, [new ImportError(r.Line, null, ex.Message)], 0, 0);
                }
            }
            await tx.CommitAsync(ct);
        }

        // Altas: una entrada por fila con lo que venía en el archivo (si la clave la genera la base, no se conoce aquí).
        await AuditAsync(ctx, info, AuditOperation.Import, parsed.Where(r => !r.Exists).Select(r =>
        {
            var row = r.Values.AsReadOnly();
            return (AuditValues.Key(info, row), (string?)null, (string?)AuditValues.Serialize(AuditValues.Columns(info, row)));
        }), ct);
        // Actualizaciones: antes y después, solo lo que cambió.
        if (befores.Count > 0)
        {
            var changes = new List<(string?, string?, string?)>();
            foreach (var r in parsed.Where(r => r.Exists))
            {
                var after = await GetByKeyAsync(ctx, info, r.Key!, ct);
                if (AuditValues.Diff(info, befores[r.Line].AsReadOnly(), after.AsReadOnly()) is var (o, n))
                    changes.Add((AuditValues.Key(info, after.AsReadOnly()), o, n));
            }
            await AuditAsync(ctx, info, AuditOperation.Update, changes, ct);
        }
        return Result(parsed.Count, [], toCreate, toUpdate);
    }

    /// <summary>Una fila del archivo ya convertida. <c>Key</c>: la clave, si viene completa (modo actualizar).</summary>
    private sealed record ImportRow(int Line, Dictionary<string, object?> Values, Dictionary<string, object?>? Key, bool Exists = false);

    /// <summary>La clave como texto invariante, para comparar lo leído del archivo con lo que devuelve la base.</summary>
    private static string KeyText(TableInfo info, IEnumerable<KeyValuePair<string, object?>> row)
    {
        var d = row.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return string.Join("\u0001", info.PrimaryKey.Select(k =>
            d.TryGetValue(k, out var v) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""));
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
