using System.Text.RegularExpressions;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Overrides;

/// <summary>Un ajuste que no se pudo aplicar. <c>Field</c> usa la ruta del JSON: "displayColumn", "columns.email.pattern".</summary>
public sealed record OverrideIssue(string Schema, string Table, string Field, string Message)
{
    public override string ToString() => $"{Schema}.{Table} · {Field}: {Message}";
}

/// <summary>
/// Combina el esquema leído del catálogo con los ajustes guardados (P-011). Es puro: no toca la base ni la metadata.
/// Lo que no se puede aplicar (una columna que ya no existe, una regex inválida) se omite y queda como aviso;
/// al guardar, los mismos avisos se devuelven como errores de validación (<see cref="Validate"/>).
/// Excepción: ocultar una columna siempre se aplica, aunque tenga avisos, porque puede proteger datos sensibles.
/// </summary>
public static partial class OverrideApplier
{
    /// <summary>Controles que entiende la UI, además de los automáticos por tipo.</summary>
    public static readonly IReadOnlySet<string> Widgets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "textarea", "email", "url", "color", "money", "password", "image" };

    /// <summary>Tiempo máximo para evaluar una expresión regular de un ajuste (evita ReDoS).</summary>
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    public static DatabaseSchema Apply(
        DatabaseSchema raw, IReadOnlyDictionary<(string Schema, string Table), TableOverride> overrides)
    {
        var byKey = overrides.ToDictionary(kv => Key(kv.Key.Schema, kv.Key.Table), kv => kv.Value);
        var issues = new List<OverrideIssue>();

        foreach (var kv in overrides.Where(kv => raw.FindTable(kv.Key.Schema, kv.Key.Table) is null))
            issues.Add(new(kv.Key.Schema, kv.Key.Table, "", "la tabla no existe en la base"));

        var tables = raw.Tables
            .Select(t => byKey.TryGetValue(Key(t.Schema, t.Name), out var o) ? ApplyTable(raw, t, o, issues) : t)
            .ToList();

        // Las FK entrantes se recalculan: un ajuste puede agregar relaciones virtuales.
        var incoming = tables.SelectMany(t => t.ForeignKeys)
            .GroupBy(fk => Key(fk.RefSchema, fk.RefTable))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ForeignKeyInfo>)g.ToList());
        tables = tables.Select(t => t with { ReferencedBy = incoming.GetValueOrDefault(Key(t.Schema, t.Name)) ?? [] }).ToList();

        // N:M (P-015): con PK y FK ya definitivas (pueden ser virtuales).
        var links = ManyToManyDetector.Detect(tables);
        var bridges = tables.Where(ManyToManyDetector.IsBridge).Select(t => Key(t.Schema, t.Name)).ToHashSet();

        tables = tables.Select(t =>
        {
            var refs = t.ReferencedBy;
            byKey.TryGetValue(Key(t.Schema, t.Name), out var own);
            // Cómo se edita cada N:M (P-087): selector salvo ajuste ("tab" para un puente con columnas propias).
            var tableLinks = (links.GetValueOrDefault(Key(t.Schema, t.Name)) ?? []).Select(l =>
            {
                var mode = own?.Links?.FirstOrDefault(kv => Same(kv.Key, l.Name)).Value;
                return mode is not null && LinkDisplay.All.Contains(mode) ? l with { Display = mode.ToLowerInvariant() } : l;
            }).ToList();
            foreach (var n in own?.Links?.Keys.Where(n => !tableLinks.Any(l => Same(l.Name, n))) ?? [])
                issues.Add(new(t.Schema, t.Name, "links", $"la relación N:M '{n}' no existe"));

            // Las tablas puente no van en pestañas, salvo que su relación se edite así: se usa el selector múltiple.
            bool AsTab(ForeignKeyInfo f) => tableLinks.Any(l => l.Display == LinkDisplay.Tab &&
                Same(l.BridgeSchema, f.Schema) && Same(l.BridgeTable, f.Table) && Same(l.LocalColumn, f.Columns[0]));
            IReadOnlyList<ForeignKeyInfo> children = refs.Where(f => !bridges.Contains(Key(f.Schema, f.Table)) || AsTab(f)).ToList();
            // Hijas elegidas por ajuste: van después de recalcular las FK entrantes, que pueden ser virtuales.
            if (byKey.TryGetValue(Key(t.Schema, t.Name), out var o) && o.Children is { } names)
            {
                children = names.Select(n => refs.FirstOrDefault(f => Same(f.Name, n))).OfType<ForeignKeyInfo>().ToList();
                foreach (var n in names.Where(n => !refs.Any(f => Same(f.Name, n))))
                    issues.Add(new(t.Schema, t.Name, "children", $"la relación '{n}' ya no existe"));
            }
            // Árbol (P-089): la autorreferencia elegida por ajuste, o la primera.
            var selfRefs = SelfReferences(t);
            return t with
            {
                Children = children,
                ManyToMany = tableLinks,
                SelfReference = (own?.TreeParent is { Length: > 0 } tp ? selfRefs.FirstOrDefault(f => Same(f.Name, tp)) : null)
                                ?? selfRefs.FirstOrDefault(),
                IsBridge = bridges.Contains(Key(t.Schema, t.Name))
            };
        }).ToList();

        // El hash sigue siendo el del catálogo: sirve para detectar cambios en la base (P-013).
        return raw with { Tables = tables, OverrideWarnings = issues.Select(i => i.ToString()).ToList() };
    }

    /// <summary>
    /// Textos por idioma (P-102): clave "en", "ru" o con región ("en-US"); los vacíos se descartan. Null si no queda ninguno.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Languages(IReadOnlyDictionary<string, string>? values, string field, Action<string, string> issue)
    {
        if (values is null) return null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, text) in values)
        {
            if (!LanguageCode().IsMatch(lang ?? "")) { issue(field, $"'{lang}' no es un código de idioma (p. ej. en o ru)"); continue; }
            if (Blank(text) is { } t) result[lang!.ToLowerInvariant()] = t;
        }
        return result.Count > 0 ? result : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9_-]{1,50}$")]
    private static partial System.Text.RegularExpressions.Regex ActionName();

    /// <summary>
    /// Las acciones contra el catálogo de rutinas (P-061): que la rutina exista y que cada parámetro esté asociado a algo.
    /// Devuelve un error por acción.
    /// </summary>
    public static Dictionary<string, string> ValidateActions(IReadOnlyList<TableAction>? actions, IReadOnlyList<RoutineInfo> routines)
    {
        var errors = new Dictionary<string, string>();
        for (var n = 0; n < (actions?.Count ?? 0); n++)
        {
            var a = actions![n];
            var r = routines.FirstOrDefault(x => Same(x.FullName, a.Routine));
            if (r is null) { errors[$"actions.{n}"] = $"la rutina '{a.Routine}' no existe o no se puede llamar (sobrecargada o con parámetros de salida)"; continue; }
            var given = a.Parameters ?? new Dictionary<string, ActionParameter>();
            if (r.Parameters.FirstOrDefault(p => !given.Any(kv => Same(kv.Key, p.Name) && (kv.Value.Column is { Length: > 0 } || kv.Value.Value is not null || kv.Value.Ask == true))) is { } missing)
                errors[$"actions.{n}"] = $"falta indicar de dónde sale el parámetro '{missing.Name}'";
            else if (given.Keys.FirstOrDefault(k => !r.Parameters.Any(p => Same(p.Name, k))) is { } extra)
                errors[$"actions.{n}"] = $"'{r.FullName}' no tiene el parámetro '{extra}'";
        }
        return errors;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$")]
    private static partial System.Text.RegularExpressions.Regex LanguageCode();

    /// <summary>FK de una columna de la tabla hacia su propia PK (p. ej. empleado → jefe): las que puede usar el árbol.</summary>
    public static IReadOnlyList<ForeignKeyInfo> SelfReferences(TableInfo t) => t.PrimaryKey.Count != 1 ? [] :
        t.ForeignKeys.Where(f => f.IsSingleColumn && Same(f.RefSchema, t.Schema) && Same(f.RefTable, t.Name) && Same(f.RefColumns[0], t.PrimaryKey[0]))
            .ToList();

    /// <summary>Valida un ajuste antes de guardarlo; lanza <see cref="ValidationException"/> con un error por campo.</summary>
    public static void Validate(DatabaseSchema raw, string schema, string table, TableOverride value)
    {
        var info = raw.FindTable(schema, table)
                   ?? throw new NotFoundException($"La tabla '{schema}.{table}' no existe.");
        var issues = new List<OverrideIssue>();
        ApplyTable(raw, info, value, issues);
        if (issues.Count > 0)
            throw new ValidationException(issues.GroupBy(i => i.Field).ToDictionary(g => g.Key, g => g.First().Message));
    }

    private static TableInfo ApplyTable(DatabaseSchema raw, TableInfo t, TableOverride o, List<OverrideIssue> issues)
    {
        void Issue(string field, string message) => issues.Add(new(t.Schema, t.Name, field, message));

        // --- Columnas ---
        var columns = t.Columns.ToList();
        foreach (var (name, co) in o.Columns ?? new Dictionary<string, ColumnOverride>())
        {
            var i = columns.FindIndex(c => Same(c.Name, name));
            if (i < 0) { Issue($"columns.{name}", "la columna no existe en la tabla"); continue; }
            columns[i] = ApplyColumn(columns[i], co, $"columns.{name}", Issue);
        }

        // --- PK virtual: solo si la base no declara una ---
        var pk = t.PrimaryKey;
        var virtualPk = false;
        if (o.VirtualPrimaryKey is { Count: > 0 } vpk)
        {
            if (t.HasPrimaryKey)
                Issue("virtualPrimaryKey", "la tabla ya tiene PK declarada en la base");
            else if (vpk.FirstOrDefault(n => !columns.Any(c => Same(c.Name, n))) is { } missing)
                Issue("virtualPrimaryKey", $"la columna '{missing}' no existe");
            else if (vpk.Select(n => columns.First(c => Same(c.Name, n))).FirstOrDefault(c => !CanBeKey(c)) is { } bad)
                Issue("virtualPrimaryKey", $"'{bad.Name}' no puede ser clave (tipo no soportado o columna oculta)");
            else
            {
                pk = vpk.Select(n => columns.First(c => Same(c.Name, n)).Name).ToList();
                virtualPk = true;
                columns = columns.Select(c => pk.Contains(c.Name) ? c with { IsPrimaryKey = true } : c).ToList();
            }
        }

        // --- FK virtuales ---
        var fks = t.ForeignKeys.ToList();
        for (var n = 0; n < (o.VirtualForeignKeys?.Count ?? 0); n++)
        {
            var v = o.VirtualForeignKeys![n];
            var field = $"virtualForeignKeys.{n}";
            if (VirtualForeignKey(raw, t, columns, v) is { } error) { Issue(field, error); continue; }

            var target = raw.FindTable(v.RefSchema, v.RefTable)!;
            var local = v.Columns.Select(c => columns.First(x => Same(x.Name, c)).Name).ToList();
            fks.Add(new ForeignKeyInfo
            {
                Name = string.IsNullOrWhiteSpace(v.Name) ? $"vfk_{t.Name}_{string.Join("_", local)}" : v.Name,
                Schema = t.Schema,
                Table = t.Name,
                Columns = local,
                RefSchema = target.Schema,
                RefTable = target.Name,
                RefColumns = v.RefColumns.Select(c => target.FindColumn(c)!.Name).ToList()
            });
        }

        // --- Filtros de los combos de FK (P-110) ---
        foreach (var (fkKey, map) in o.LookupFilters ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            var field = $"lookupFilters.{fkKey}";
            var cols = fkKey.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var i = fks.FindIndex(f => f.Columns.SequenceEqual(cols, StringComparer.OrdinalIgnoreCase));
            if (i < 0) { Issue(field, $"no hay una relación con las columnas '{fkKey}'"); continue; }
            var fk = fks[i];
            var target = raw.FindTable(fk.RefSchema, fk.RefTable);
            var filters = new List<LookupFilter>();
            foreach (var (refName, localName) in map ?? new Dictionary<string, string>())
            {
                var refCol = target?.FindColumn(refName ?? "");
                var local = columns.FirstOrDefault(c => Same(c.Name, localName ?? ""));
                if (refCol is null) Issue(field, $"la columna '{refName}' no existe en {fk.RefSchema}.{fk.RefTable}");
                else if (local is null) Issue(field, $"la columna '{localName}' no existe");
                else if (fk.Columns.Contains(local.Name, StringComparer.OrdinalIgnoreCase)) Issue(field, $"'{local.Name}' es la misma relación: no puede filtrarla");
                else if (refCol.Type is LogicalType.Unsupported or LogicalType.Binary or LogicalType.Json) Issue(field, $"no se puede filtrar por '{refCol.Name}'");
                else filters.Add(new LookupFilter(refCol.Name, local.Name));
            }
            fks[i] = fk with { LookupFilters = filters };
        }

        // --- Columna a mostrar: la del ajuste, o se vuelve a elegir (pudo quedar oculta) ---
        var display = DisplayColumnResolver.Resolve(columns, fks.SelectMany(f => f.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (o.DisplayColumn is { Length: > 0 } dc)
        {
            var col = columns.FirstOrDefault(c => Same(c.Name, dc));
            if (col is null) Issue("displayColumn", $"la columna '{dc}' no existe");
            else if (!col.IsSelectable) Issue("displayColumn", "la columna está oculta o su tipo no se puede mostrar");
            else display = col.Name;
        }

        // --- Orden por defecto ---
        string? defaultSort = null;
        if (o.DefaultSort is { Length: > 0 } ds)
        {
            var specs = SortSpec.ParseList(ds);
            var bad = specs.FirstOrDefault(s => columns.FirstOrDefault(c => Same(c.Name, s.Column)) is not { IsSortable: true });
            if (bad is not null) Issue("defaultSort", $"no se puede ordenar por '{bad.Column}'");
            else defaultSort = string.Join(",", specs.Select(s => (s.Descending ? "-" : "") + columns.First(c => Same(c.Name, s.Column)).Name));
        }

        // --- Árbol (P-089) y relaciones N:M (P-087) ---
        if (o.TreeParent is { Length: > 0 } treeParent &&
            !SelfReferences(t with { ForeignKeys = fks, PrimaryKey = pk }).Any(f => Same(f.Name, treeParent)))
            Issue("treeParent", $"'{treeParent}' no es una relación de la tabla consigo misma");
        foreach (var (name, mode) in o.Links ?? new Dictionary<string, string>())
            if (!LinkDisplay.All.Contains(mode ?? ""))
                Issue("links", $"'{mode}' no es válido para '{name}'; use selector, tab o none");

        // --- Acciones (P-061): nombre y columnas; la rutina y sus parámetros se validan contra el catálogo al guardar ---
        var actions = new List<TableAction>();
        foreach (var (a, n) in (o.Actions ?? []).Select((a, n) => (a, n)))
        {
            var field = $"actions.{n}";
            if (!ActionName().IsMatch(a.Name ?? "")) { Issue(field, "el nombre usa minúsculas, números, - o _"); continue; }
            if (actions.Any(x => Same(x.Name, a.Name!))) { Issue(field, $"la acción '{a.Name}' está repetida"); continue; }
            if ((a.Routine ?? "").Split('.') is not [{ Length: > 0 }, { Length: > 0 }]) { Issue(field, "la rutina se escribe como esquema.nombre"); continue; }
            var bad = (a.Parameters ?? new Dictionary<string, ActionParameter>()).FirstOrDefault(kv =>
                kv.Value.Column is { Length: > 0 } col && !columns.Any(c => Same(c.Name, col)));
            if (bad.Key is not null) { Issue(field, $"la columna '{bad.Value.Column}' no existe"); continue; }
            actions.Add(a with { Labels = Languages(a.Labels, $"{field}.labels", Issue) });
        }

        // --- Avisos por correo (D-073) ---
        Notifications.TableNotify? notify = null;
        if (o.Notify is { } no)
        {
            var emails = (no.Emails ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ops = new HashSet<Audit.AuditOperation>();
            foreach (var name in no.On ?? [])
                if (NotifyOps.TryGetValue(name.Trim(), out var op)) ops.Add(op);
                else Issue("notify.on", $"'{name}' no es válido; use create, update, delete, import o links");
            if (emails.FirstOrDefault(e => !Email().IsMatch(e)) is { } badEmail) Issue("notify.emails", $"'{badEmail}' no es un correo válido");
            else if (emails.Count > 0)
                notify = new Notifications.TableNotify
                {
                    Emails = emails,
                    On = ops.Count > 0 ? ops : new HashSet<Audit.AuditOperation> { Audit.AuditOperation.Create, Audit.AuditOperation.Update, Audit.AuditOperation.Delete },
                    IncludeValues = no.IncludeValues ?? true
                };
        }

        return t with
        {
            Notify = notify,
            Columns = columns.OrderBy(c => c.Order).ThenBy(c => c.Ordinal).ToList(),
            PrimaryKey = pk,
            HasVirtualPrimaryKey = virtualPk,
            ForeignKeys = fks,
            DisplayColumn = display,
            DefaultSort = defaultSort,
            Label = Blank(o.Label),
            Labels = Languages(o.Labels, "labels", Issue),
            Actions = actions,
            Group = Blank(o.Group),
            Hidden = o.Hidden ?? false,
            ForcedReadOnly = o.ReadOnly ?? false
        };
    }

    private static readonly IReadOnlyDictionary<string, Audit.AuditOperation> NotifyOps =
        new Dictionary<string, Audit.AuditOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] = Audit.AuditOperation.Create, ["update"] = Audit.AuditOperation.Update, ["delete"] = Audit.AuditOperation.Delete,
            ["import"] = Audit.AuditOperation.Import, ["links"] = Audit.AuditOperation.Links
        };

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();

    private static ColumnInfo ApplyColumn(ColumnInfo c, ColumnOverride o, string field, Action<string, string> issue)
    {
        var isText = c.Type is LogicalType.String or LogicalType.Text;
        var isNumber = c.Type is LogicalType.Integer or LogicalType.Decimal && !c.IsLargeNumber;

        var r = c with
        {
            Label = Blank(o.Label) ?? c.Label,
            HelpText = Blank(o.HelpText) ?? c.HelpText,
            Labels = Languages(o.Labels, $"{field}.labels", issue) ?? c.Labels,
            HelpTexts = Languages(o.HelpTexts, $"{field}.helpTexts", issue) ?? c.HelpTexts,
            Hidden = o.Hidden ?? c.Hidden,
            ShowInGrid = o.ShowInGrid ?? c.ShowInGrid,
            Order = o.Order ?? c.Order,
            ForcedReadOnly = o.ReadOnly ?? c.ForcedReadOnly
        };

        if (r.Hidden && !c.IsPrimaryKey && c.IsRequiredOnInsert)
            issue($"{field}.hidden", "es obligatoria y no tiene valor por defecto: al ocultarla no se podrán crear registros");

        if (o.Required == true)
        {
            if (c.IsReadOnly) issue($"{field}.required", "la columna es de solo lectura");
            else r = r with { IsNullable = false, ForcedRequired = true };
        }

        if (Blank(o.Widget) is { } w)
        {
            if (!Widgets.Contains(w)) issue($"{field}.widget", $"control desconocido; use: {string.Join(", ", Widgets)}");
            else if (Same(w, "image") && !c.IsFile) issue($"{field}.widget", "image solo se aplica a columnas de archivo");
            else r = r with { Widget = w.ToLowerInvariant() };
        }

        if (Blank(o.Pattern) is { } pattern)
        {
            if (!isText) issue($"{field}.pattern", "solo se aplica a columnas de texto");
            else if (!IsValidPattern(pattern, out var error)) issue($"{field}.pattern", $"expresión regular inválida: {error}");
            else r = r with { Pattern = pattern };
        }

        if (o.MaxLength is { } len)
        {
            if (!isText) issue($"{field}.maxLength", "solo se aplica a columnas de texto");
            else if (len < 1) issue($"{field}.maxLength", "debe ser mayor que cero");
            // Nunca más largo que lo que acepta la base.
            else r = r with { MaxLength = c.MaxLength is { } m ? Math.Min(m, len) : len };
        }

        if (o.Min is not null || o.Max is not null)
        {
            if (!isNumber) issue($"{field}.{(o.Min is not null ? "min" : "max")}", "solo se aplica a columnas numéricas");
            else if (o.Min > o.Max) issue($"{field}.min", "el mínimo es mayor que el máximo");
            else r = r with { Min = o.Min, Max = o.Max };
        }

        if (o.Options is { Count: > 0 } options)
        {
            if (!isText) issue($"{field}.options", "solo se aplica a columnas de texto");
            else r = r with { Options = options.Distinct().ToList() };
        }

        return r;
    }

    private static string? VirtualForeignKey(DatabaseSchema raw, TableInfo t, IReadOnlyList<ColumnInfo> columns, VirtualForeignKey v)
    {
        if (v.Columns is not { Count: > 0 } || v.RefColumns is not { Count: > 0 }) return "faltan las columnas";
        if (v.Columns.Count != v.RefColumns.Count) return "las columnas locales y las referenciadas no son la misma cantidad";
        if (v.Columns.FirstOrDefault(n => !columns.Any(c => Same(c.Name, n))) is { } missing) return $"la columna '{missing}' no existe";
        if (raw.FindTable(v.RefSchema ?? "", v.RefTable ?? "") is not { } target) return $"la tabla '{v.RefSchema}.{v.RefTable}' no existe";
        if (v.RefColumns.FirstOrDefault(n => target.FindColumn(n) is null) is { } refMissing)
            return $"la columna '{refMissing}' no existe en {target.FullName}";
        if (t.ForeignKeys.Any(fk => fk.Columns.SequenceEqual(v.Columns, StringComparer.OrdinalIgnoreCase)))
            return "esas columnas ya tienen una FK declarada en la base";
        return null;
    }

    public static bool IsValidPattern(string pattern, out string? error)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, PatternTimeout);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>¿El texto cumple el patrón completo? (como el atributo <c>pattern</c> de HTML).</summary>
    public static bool MatchesPattern(string value, string pattern) =>
        Regex.IsMatch(value, $"^(?:{pattern})$", RegexOptions.None, PatternTimeout);

    private static bool CanBeKey(ColumnInfo c) =>
        c.Type is not (LogicalType.Unsupported or LogicalType.Binary or LogicalType.Json or LogicalType.Text) && !c.Hidden;

    private static string Key(string schema, string table) => $"{schema}\u0001{table}".ToLowerInvariant();
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
