using System.Text.RegularExpressions;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Overrides;

/// <summary>Tipo de sugerencia (P-112): la UI arma el texto en el idioma del usuario con los datos.</summary>
public enum SuggestionKind
{
    /// <summary>Una columna que parece FK sin relación declarada: TipoGastoID → TipoGasto.</summary>
    Relation,
    /// <summary>Un combo que conviene acotar por otra columna: AreaID por EmpresaID.</summary>
    LookupFilter,
    /// <summary>Columna técnica (fecha de creación, usuario, host…): solo lectura y fuera de la grilla.</summary>
    Technical,
    /// <summary>Correo electrónico: patrón de validación.</summary>
    Email
}

/// <param name="Id">Identificador estable de la sugerencia (para marcarla en la UI).</param>
/// <param name="Column">Columna de esta tabla a la que se refiere.</param>
/// <param name="RefSchema">Tabla referenciada (relaciones y filtros).</param>
/// <param name="RefColumn">Columna de la tabla referenciada (PK en relaciones, columna que filtra en filtros).</param>
/// <param name="LocalColumn">Filtros: columna de este registro que da el valor.</param>
/// <param name="Patch">Ajuste parcial que se combina con el actual al aceptarla.</param>
/// <param name="Requires">Otra sugerencia sin la cual esta no aplica (un filtro sobre una relación que también es sugerida).</param>
public sealed record Suggestion(string Id, SuggestionKind Kind, string Column, TableOverride Patch,
    string? RefSchema = null, string? RefTable = null, string? RefColumn = null, string? LocalColumn = null, string? Requires = null);

/// <summary>
/// Sugerencias de ajustes sin IA (P-112, D-099): reglas sobre el catálogo, sin mirar datos ni salir de la app.
/// <list type="bullet">
/// <item>Relaciones: una columna sin FK cuyo nombre es la PK de otra tabla (CentroCosto.TipoGastoID → TipoGasto.TipoGastoID)
/// o nombra a otra tabla (empresa_id → empresa.id), con el mismo tipo.</item>
/// <item>Filtros de combos: la tabla referenciada tiene una columna que va a la misma tabla que otra de esta
/// (Area.EmpresaID y CentroCosto.EmpresaID → Empresa).</item>
/// <item>Columnas técnicas (CreationDate, ModifiedBy, HostName, RowStatus…): solo lectura y fuera de la grilla.</item>
/// <item>Correos: patrón de validación.</item>
/// </list>
/// Lo que ya está ajustado no se vuelve a sugerir.
/// </summary>
public static partial class OverrideSuggester
{
    public const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    private static readonly HashSet<string> TechnicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "creationdate", "createdate", "createdat", "created", "createdon", "fechacreacion", "fechaalta", "fecharegistro",
        "modifieddate", "modifydate", "modifiedat", "updatedat", "updated", "updatedon", "modifiedon", "lastmodified",
        "fechamodificacion", "fechaactualizacion",
        "createdby", "creadopor", "usuariocreacion", "modifiedby", "updatedby", "modificadopor", "usuariomodificacion",
        "hostname", "host", "ipaddress", "ip", "terminal", "workstation", "equipo",
        "rowstatus", "rowversion", "timestamp", "rowguid", "syncstatus", "lastsync"
    };

    public static IReadOnlyList<Suggestion> For(DatabaseSchema schema, TableInfo table, TableOverride? current)
    {
        current ??= new TableOverride();
        var result = new List<Suggestion>();
        var fkColumns = table.ForeignKeys.SelectMany(f => f.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // --- Relaciones por nombre ---
        var relations = new List<(ColumnInfo Column, TableInfo Target, ColumnInfo Key)>();
        foreach (var c in table.Columns.Where(c => !fkColumns.Contains(c.Name) && !c.IsPrimaryKey && c.IsSelectable))
        {
            if (FindTarget(schema, table, c) is { } target)
            {
                relations.Add((c, target.Table, target.Key));
                result.Add(new Suggestion($"relation:{c.Name}", SuggestionKind.Relation, c.Name,
                    new TableOverride { VirtualForeignKeys = [new VirtualForeignKey([c.Name], target.Table.Schema, target.Table.Name, [target.Key.Name])] },
                    target.Table.Schema, target.Table.Name, target.Key.Name));
            }
        }

        // --- Filtros de combos: con las FK que ya hay y con las sugeridas ---
        var allFks = table.ForeignKeys.Where(f => f.IsSingleColumn)
            .Select(f => (Column: f.Columns[0], f.RefSchema, f.RefTable, Filtered: f.LookupFilters.Count > 0, Suggested: false))
            .Concat(relations.Select(r => (Column: r.Column.Name, RefSchema: r.Target.Schema, RefTable: r.Target.Name, Filtered: false, Suggested: true)))
            .ToList();
        foreach (var fk in allFks.Where(f => !f.Filtered && !(current.LookupFilters?.Keys.Any(k => Same(k, f.Column)) ?? false)))
        {
            var target = schema.FindTable(fk.RefSchema, fk.RefTable);
            if (target is null) continue;
            // Qué tabla apunta cada columna de la tabla referenciada (por FK real o por nombre).
            foreach (var tc in target.Columns.Where(x => !x.IsPrimaryKey))
            {
                var pointsTo = target.ForeignKeys.FirstOrDefault(f => f.IsSingleColumn && Same(f.Columns[0], tc.Name)) is { } tf
                    ? (tf.RefSchema, tf.RefTable)
                    : FindTarget(schema, target, tc) is { } guess ? (guess.Table.Schema, guess.Table.Name) : default;
                if (pointsTo == default) continue;
                var local = allFks.FirstOrDefault(o => !Same(o.Column, fk.Column) && Same(o.RefSchema, pointsTo.Item1) && Same(o.RefTable, pointsTo.Item2));
                if (local.Column is null) continue;
                result.Add(new Suggestion($"filter:{fk.Column}:{tc.Name}", SuggestionKind.LookupFilter, fk.Column,
                    new TableOverride
                    {
                        LookupFilters = new Dictionary<string, IReadOnlyDictionary<string, string>>
                        {
                            [fk.Column] = new Dictionary<string, string> { [tc.Name] = local.Column }
                        }
                    },
                    target.Schema, target.Name, tc.Name, local.Column,
                    Requires: fk.Suggested ? $"relation:{fk.Column}" : null));
            }
        }

        // --- Columnas técnicas y correos ---
        foreach (var c in table.Columns.Where(c => !c.IsPrimaryKey && !fkColumns.Contains(c.Name)))
        {
            var o = current.Columns?.FirstOrDefault(kv => Same(kv.Key, c.Name)).Value;
            var key = Compact(c.Name);
            if (TechnicalNames.Contains(key) && o?.ReadOnly is null && o?.ShowInGrid is null && !c.IsReadOnly)
                result.Add(new Suggestion($"technical:{c.Name}", SuggestionKind.Technical, c.Name,
                    new TableOverride { Columns = new Dictionary<string, ColumnOverride> { [c.Name] = new() { ReadOnly = true, ShowInGrid = false } } }));
            if (c.Type is LogicalType.String && EmailName().IsMatch(key) && o?.Pattern is null)
                result.Add(new Suggestion($"email:{c.Name}", SuggestionKind.Email, c.Name,
                    new TableOverride { Columns = new Dictionary<string, ColumnOverride> { [c.Name] = new() { Pattern = EmailPattern } } }));
        }
        return result;
    }

    /// <summary>
    /// La tabla a la que parece apuntar una columna: la que tiene una PK de una columna con su mismo nombre
    /// (TipoGastoID) o la que se llama como la columna sin el sufijo de id (empresa_id → empresa), del mismo tipo.
    /// Se prefiere el mismo esquema; si hay más de una candidata igual de buena, no se sugiere.
    /// </summary>
    private static (TableInfo Table, ColumnInfo Key)? FindTarget(DatabaseSchema schema, TableInfo owner, ColumnInfo c)
    {
        if (c.Type is not (LogicalType.Integer or LogicalType.String or LogicalType.Uuid)) return null;
        var baseName = BaseName(c.Name);
        if (baseName is null) return null;
        var candidates = new List<(TableInfo Table, ColumnInfo Key, int Score)>();
        foreach (var t in schema.Tables.Where(t => t.Kind == TableKind.Table && t.PrimaryKey.Count == 1))
        {
            var key = t.FindColumn(t.PrimaryKey[0]);
            if (key is null || key.Type != c.Type) continue;
            if (ReferenceEquals(t, owner) || (Same(t.Schema, owner.Schema) && Same(t.Name, owner.Name)))
                continue; // las autorreferencias (Parent, Jefe) no se adivinan por nombre
            var score = Same(Compact(key.Name), Compact(c.Name)) && Compact(key.Name) != "id" ? 3
                : Singular(Compact(t.Name)) == Singular(baseName) ? 2 : 0;
            if (score == 0) continue;
            if (Same(t.Schema, owner.Schema)) score += 1;
            candidates.Add((t, key, score));
        }
        if (candidates.Count == 0) return null;
        var best = candidates.Max(x => x.Score);
        var top = candidates.Where(x => x.Score == best).ToList();
        return top.Count == 1 ? (top[0].Table, top[0].Key) : null;
    }

    /// <summary>"TipoGastoID" → "tipogasto"; "empresa_id" → "empresa"; "id_area" → "area"; "Nombre" → null.</summary>
    private static string? BaseName(string column)
    {
        var compact = Compact(column);
        if (compact.Length > 2 && compact.EndsWith("id")) return compact[..^2];
        if (compact.Length > 2 && compact.StartsWith("id") && column.Contains('_')) return compact[2..];
        return null;
    }

    /// <summary>
    /// Las sugerencias que se pueden aplicar sobre el ajuste actual, validadas con el catálogo. Una que depende de
    /// otra (un filtro sobre una relación sugerida) se valida junto con ella.
    /// </summary>
    public static IReadOnlyList<Suggestion> Valid(DatabaseSchema raw, string schema, string table, TableOverride current, IReadOnlyList<Suggestion> all)
    {
        bool Ok(TableOverride o)
        {
            try { OverrideApplier.Validate(raw, schema, table, o); return true; }
            catch (Errors.ValidationException) { return false; }
        }
        var valid = new List<Suggestion>();
        foreach (var sg in all)
        {
            var with = sg.Requires is { } req && all.FirstOrDefault(x => x.Id == req) is { } dep ? Merge(current, dep.Patch) : current;
            if (Ok(Merge(with, sg.Patch))) valid.Add(sg);
        }
        // Si la relación de la que depende un filtro no quedó, el filtro tampoco.
        return valid.Where(sg => sg.Requires is null || valid.Any(x => x.Id == sg.Requires)).ToList();
    }

    /// <summary>Suma un ajuste parcial (de una sugerencia) al actual: relaciones y filtros se agregan, columnas se combinan.</summary>
    public static TableOverride Merge(TableOverride current, TableOverride patch)
    {
        var vfks = (current.VirtualForeignKeys ?? []).ToList();
        foreach (var v in patch.VirtualForeignKeys ?? [])
            if (!vfks.Any(x => x.Columns.SequenceEqual(v.Columns, StringComparer.OrdinalIgnoreCase))) vfks.Add(v);
        var filters = (current.LookupFilters ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, map) in patch.LookupFilters ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            var merged = filters.TryGetValue(k, out var m) ? m.ToDictionary(x => x.Key, x => x.Value) : new Dictionary<string, string>();
            foreach (var (rc, lc) in map) merged[rc] = lc;
            filters[k] = merged;
        }
        var columns = (current.Columns ?? new Dictionary<string, ColumnOverride>()).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, co) in patch.Columns ?? new Dictionary<string, ColumnOverride>())
        {
            var c = columns.TryGetValue(name, out var existing) ? existing : new ColumnOverride();
            columns[name] = c with
            {
                // Lo que el usuario ya decidió no se pisa.
                ReadOnly = c.ReadOnly ?? co.ReadOnly, ShowInGrid = c.ShowInGrid ?? co.ShowInGrid, Hidden = c.Hidden ?? co.Hidden,
                Pattern = c.Pattern ?? co.Pattern, Label = c.Label ?? co.Label, Labels = c.Labels ?? co.Labels, Widget = c.Widget ?? co.Widget
            };
        }
        return current with
        {
            VirtualForeignKeys = vfks.Count > 0 ? vfks : null,
            LookupFilters = filters.Count > 0 ? filters : null,
            Columns = columns.Count > 0 ? columns : null
        };
    }

    private static string Compact(string name) => name.Replace("_", "").Replace(" ", "").ToLowerInvariant();

    /// <summary>"areas" → "area", "empresas" → "empresa", "sucursales" → "sucursal".</summary>
    private static string Singular(string name) =>
        name.EndsWith("es") && name.Length > 4 && !name.EndsWith("ses") ? name[..^2]
        : name.EndsWith('s') && name.Length > 3 ? name[..^1] : name;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("(email|correo|^mail)")]
    private static partial Regex EmailName();
}
