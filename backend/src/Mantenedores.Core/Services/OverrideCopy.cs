using Mantenedores.Core.Errors;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

/// <summary>Qué pasa con el ajuste de una tabla al copiar (P-111).</summary>
public enum OverrideCopyStatus
{
    /// <summary>La tabla destino no tenía ajuste: se crea.</summary>
    New,
    /// <summary>La tabla destino tenía ajuste: se reemplaza.</summary>
    Replace,
    /// <summary>La tabla no existe en el destino: no se copia.</summary>
    MissingTable,
    /// <summary>El ajuste no es válido en el destino (p. ej. falta una columna): no se copia.</summary>
    Invalid,
    /// <summary>Solo con "reemplazar todo": la tabla destino tenía ajuste y el origen no; se borra.</summary>
    Removed
}

public sealed record OverrideCopyItem(string Schema, string Table, string TargetSchema, OverrideCopyStatus Status,
    IReadOnlyDictionary<string, string>? Errors = null);

public sealed record OverrideCopyResult(IReadOnlyList<OverrideCopyItem> Items, bool Applied)
{
    public int Copied => Items.Count(i => i.Status is OverrideCopyStatus.New or OverrideCopyStatus.Replace);
}

/// <summary>
/// Copia los ajustes de todas las tablas de una conexión a otra (P-111, D-098), p. ej. la misma base en otro servidor.
/// Cada ajuste se valida contra el catálogo del destino, como al guardarlo a mano. Los esquemas pueden cambiar de
/// nombre (en MySQL el esquema es la base: ventas → ventas_qa). Las relaciones elegidas por nombre (pestañas hijas,
/// árbol) se buscan en el destino por sus columnas y tablas, porque los nombres automáticos de las FK cambian de un
/// servidor a otro.
/// </summary>
public sealed class OverrideCopier(SchemaService schemas)
{
    /// <summary>Partes de un ajuste que se pueden copiar por separado.</summary>
    public static readonly IReadOnlyList<string> Parts = ["relations", "table", "columns", "actions", "notify"];

    /// <param name="parts">
    /// Qué copiar (null = todo): <c>relations</c> (PK y FK virtuales, filtros de combos, pestañas hijas, árbol, N:M),
    /// <c>table</c> (nombre, grupo, columna a mostrar, orden, oculta, solo lectura), <c>columns</c>, <c>actions</c> y
    /// <c>notify</c>. Lo que no se copia queda como estaba en el destino.
    /// </param>
    /// <param name="tables">Solo estas tablas del origen ("esquema.tabla"); null = todas.</param>
    public async Task<OverrideCopyResult> CopyAsync(string from, string to, IReadOnlyDictionary<string, string>? schemaMap,
        bool replaceAll, bool apply, string? user, CancellationToken ct,
        IReadOnlyCollection<string>? parts = null, IReadOnlyCollection<string>? tables = null)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("from", "elija una conexión distinta de la de destino");
        if (parts?.FirstOrDefault(x => !Parts.Contains(x, StringComparer.OrdinalIgnoreCase)) is { } badPart)
            throw new ValidationException("parts", $"'{badPart}' no es válido; use {string.Join(", ", Parts)}");
        if (parts is { Count: 0 }) throw new ValidationException("parts", "elija al menos una parte para copiar");
        var allParts = parts is null || Parts.All(x => parts.Contains(x, StringComparer.OrdinalIgnoreCase));
        // "Reemplazar todo" borra ajustes enteros: solo tiene sentido copiando todo, de todas las tablas.
        replaceAll = replaceAll && allParts && tables is null;

        // Solo entre bases del mismo motor: tipos, nombres y rutinas no se traducen de un motor a otro.
        var (sourceEngine, targetEngine) = ((await schemas.GetDefinitionAsync(from, ct)).Engine, (await schemas.GetDefinitionAsync(to, ct)).Engine);
        if (sourceEngine != targetEngine)
            throw new ValidationException("from",
                $"solo se copian ajustes entre conexiones del mismo motor ({sourceEngine} y {targetEngine} son distintos)");
        var sourceCtx = await schemas.GetUnrestrictedContextAsync(from, ct);
        var sourceOverrides = await schemas.Overrides.ListAsync(from, ct);
        var source = sourceCtx.Schema;
        var targetRaw = await schemas.GetRawSchemaAsync(to, ct);
        var targetOverrides = await schemas.Overrides.ListAsync(to, ct);
        var routines = sourceOverrides.Values.Any(o => o.Actions is { Count: > 0 }) ? await schemas.GetRoutinesAsync(to, ct) : [];

        string MapSchema(string s) =>
            schemaMap?.FirstOrDefault(kv => Same(kv.Key, s)).Value is { Length: > 0 } m ? m : s;

        // 1) Esquemas cambiados y tabla destino de cada ajuste.
        var planned = new List<(OverrideCopyItem Item, TableInfo? Target, TableOverride Value)>();
        foreach (var ((schema, table), value) in sourceOverrides.OrderBy(kv => kv.Key.Schema).ThenBy(kv => kv.Key.Table))
        {
            if (tables is not null && !tables.Any(x => Same(x, $"{schema}.{table}"))) continue;
            var targetSchema = MapSchema(schema);
            var target = targetRaw.FindTable(targetSchema, table);
            var item = new OverrideCopyItem(schema, table, target?.Schema ?? targetSchema, OverrideCopyStatus.MissingTable);
            var copied = Remap(value, MapSchema);
            if (!allParts && target is not null)
            {
                var current = targetOverrides.FirstOrDefault(kv => Same(kv.Key.Schema, target.Schema) && Same(kv.Key.Table, target.Name)).Value;
                copied = Merge(current ?? new TableOverride(), copied, parts!);
            }
            planned.Add((item, target, copied));
        }

        // 2) Nombres de relaciones: se traducen con el destino ya combinado con los ajustes copiados (FK virtuales).
        var draft = planned.Where(p => p.Target is not null).ToDictionary(p => (p.Target!.Schema, p.Target.Name), p => p.Value);
        var targetApplied = OverrideApplier.Apply(targetRaw, draft);
        var sourceFks = source.Tables.SelectMany(t => t.ForeignKeys).ToList();
        var targetFks = targetApplied.Tables.SelectMany(t => t.ForeignKeys).ToList();
        string Translate(string name)
        {
            var fk = sourceFks.FirstOrDefault(f => Same(f.Name, name));
            if (fk is null) return name;
            var match = targetFks.FirstOrDefault(f =>
                Same(f.Schema, MapSchema(fk.Schema)) && Same(f.Table, fk.Table) && Same(f.RefSchema, MapSchema(fk.RefSchema)) &&
                Same(f.RefTable, fk.RefTable) && f.Columns.SequenceEqual(fk.Columns, StringComparer.OrdinalIgnoreCase));
            return match?.Name ?? name;
        }

        // 3) Validación contra el catálogo del destino, como al guardar a mano.
        var items = new List<OverrideCopyItem>();
        var toSave = new List<(TableInfo Target, TableOverride Value)>();
        foreach (var (item, target, value) in planned)
        {
            if (target is null) { items.Add(item); continue; }
            var translated = value with
            {
                Children = value.Children?.Select(Translate).ToList(),
                TreeParent = value.TreeParent is { Length: > 0 } tp ? Translate(tp) : value.TreeParent
            };
            var errors = new Dictionary<string, string>();
            try { OverrideApplier.Validate(targetRaw, target.Schema, target.Name, translated); }
            catch (ValidationException ex) { foreach (var (k, v) in ex.Errors) errors[k] = v; }
            if (translated.Actions is { Count: > 0 })
                foreach (var (k, v) in OverrideApplier.ValidateActions(translated.Actions, routines)) errors[k] = v;
            if (errors.Count > 0) { items.Add(item with { Status = OverrideCopyStatus.Invalid, Errors = errors }); continue; }

            var existed = targetOverrides.Keys.Any(k => Same(k.Schema, target.Schema) && Same(k.Table, target.Name));
            items.Add(item with { Status = existed ? OverrideCopyStatus.Replace : OverrideCopyStatus.New });
            toSave.Add((target, translated));
        }

        // 4) Con "reemplazar todo", lo que el destino tiene y el origen no se borra.
        var removed = replaceAll
            ? targetOverrides.Keys.Where(k => !toSave.Any(s => Same(s.Target.Schema, k.Schema) && Same(s.Target.Name, k.Table))
                                              && !items.Any(i => i.Status == OverrideCopyStatus.Invalid && Same(i.TargetSchema, k.Schema) && Same(i.Table, k.Table)))
                .ToList()
            : [];
        items.AddRange(removed.Select(k => new OverrideCopyItem(k.Schema, k.Table, k.Schema, OverrideCopyStatus.Removed)));

        if (!apply) return new OverrideCopyResult(items, false);

        foreach (var (target, value) in toSave)
        {
            // La clave del almacén distingue mayúsculas: se borra cualquier variante antes de guardar.
            foreach (var k in targetOverrides.Keys.Where(k => Same(k.Schema, target.Schema) && Same(k.Table, target.Name)))
                await schemas.Overrides.DeleteAsync(to, k.Schema, k.Table, ct);
            await schemas.Overrides.SaveAsync(to, target.Schema, target.Name, value, user, ct);
        }
        foreach (var k in removed) await schemas.Overrides.DeleteAsync(to, k.Schema, k.Table, ct);
        if (toSave.Count > 0 || removed.Count > 0) await schemas.OverridesChangedAsync(to, ct);
        return new OverrideCopyResult(items, true);
    }

    /// <summary>El ajuste del destino con las partes elegidas tomadas del origen.</summary>
    public static TableOverride Merge(TableOverride target, TableOverride source, IReadOnlyCollection<string> parts)
    {
        bool Has(string part) => parts.Contains(part, StringComparer.OrdinalIgnoreCase);
        var r = target;
        if (Has("relations"))
            r = r with
            {
                VirtualPrimaryKey = source.VirtualPrimaryKey, VirtualForeignKeys = source.VirtualForeignKeys, LookupFilters = source.LookupFilters,
                Children = source.Children, TreeParent = source.TreeParent, Links = source.Links
            };
        if (Has("table"))
            r = r with
            {
                Label = source.Label, Labels = source.Labels, Group = source.Group, DisplayColumn = source.DisplayColumn,
                DefaultSort = source.DefaultSort, Hidden = source.Hidden, ReadOnly = source.ReadOnly
            };
        if (Has("columns")) r = r with { Columns = source.Columns };
        if (Has("actions")) r = r with { Actions = source.Actions };
        if (Has("notify")) r = r with { Notify = source.Notify };
        return r;
    }

    /// <summary>Cambia el esquema donde el ajuste nombra otra tabla: FK virtuales y rutinas de las acciones.</summary>
    public static TableOverride Remap(TableOverride o, Func<string, string> mapSchema) => o with
    {
        VirtualForeignKeys = o.VirtualForeignKeys?.Select(v => v with { RefSchema = mapSchema(v.RefSchema) }).ToList(),
        Actions = o.Actions?.Select(a => a.Routine is { } r && r.Split('.') is [var s, var n] ? a with { Routine = $"{mapSchema(s)}.{n}" } : a).ToList()
    };

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
