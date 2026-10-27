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
public static class OverrideApplier
{
    /// <summary>Controles que entiende la UI, además de los automáticos por tipo.</summary>
    public static readonly IReadOnlySet<string> Widgets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "textarea", "email", "url", "color", "money", "password" };

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
            // Las tablas puente no van en pestañas por defecto: se editan con el selector múltiple.
            IReadOnlyList<ForeignKeyInfo> children = refs.Where(f => !bridges.Contains(Key(f.Schema, f.Table))).ToList();
            // Hijas elegidas por ajuste: van después de recalcular las FK entrantes, que pueden ser virtuales.
            if (byKey.TryGetValue(Key(t.Schema, t.Name), out var o) && o.Children is { } names)
            {
                children = names.Select(n => refs.FirstOrDefault(f => Same(f.Name, n))).OfType<ForeignKeyInfo>().ToList();
                foreach (var n in names.Where(n => !refs.Any(f => Same(f.Name, n))))
                    issues.Add(new(t.Schema, t.Name, "children", $"la relación '{n}' ya no existe"));
            }
            return t with
            {
                Children = children,
                ManyToMany = links.GetValueOrDefault(Key(t.Schema, t.Name)) ?? [],
                IsBridge = bridges.Contains(Key(t.Schema, t.Name))
            };
        }).ToList();

        // El hash sigue siendo el del catálogo: sirve para detectar cambios en la base (P-013).
        return raw with { Tables = tables, OverrideWarnings = issues.Select(i => i.ToString()).ToList() };
    }

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

        return t with
        {
            Columns = columns.OrderBy(c => c.Order).ThenBy(c => c.Ordinal).ToList(),
            PrimaryKey = pk,
            HasVirtualPrimaryKey = virtualPk,
            ForeignKeys = fks,
            DisplayColumn = display,
            DefaultSort = defaultSort,
            Label = Blank(o.Label),
            Group = Blank(o.Group),
            Hidden = o.Hidden ?? false,
            ForcedReadOnly = o.ReadOnly ?? false
        };
    }

    private static ColumnInfo ApplyColumn(ColumnInfo c, ColumnOverride o, string field, Action<string, string> issue)
    {
        var isText = c.Type is LogicalType.String or LogicalType.Text;
        var isNumber = c.Type is LogicalType.Integer or LogicalType.Decimal && !c.IsLargeNumber;

        var r = c with
        {
            Label = Blank(o.Label) ?? c.Label,
            HelpText = Blank(o.HelpText) ?? c.HelpText,
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
