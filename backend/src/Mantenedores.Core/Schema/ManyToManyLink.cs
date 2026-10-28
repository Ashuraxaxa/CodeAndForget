namespace Mantenedores.Core.Schema;

/// <summary>
/// Relación N:M de una tabla a través de una tabla puente (P-015), p. ej. producto ⇄ etiqueta vía producto_etiqueta.
/// <c>LocalColumn</c> es la columna del puente que apunta a esta tabla; <c>ForeignColumn</c>, la que apunta a la otra.
/// </summary>
public sealed record ManyToManyLink(
    string Name,
    string BridgeSchema, string BridgeTable,
    string LocalColumn, string ForeignColumn,
    string TargetSchema, string TargetTable, string TargetColumn)
{
    /// <summary>Cómo se edita (P-087): <see cref="LinkDisplay.Selector"/>, <see cref="LinkDisplay.Tab"/> o <see cref="LinkDisplay.None"/>.</summary>
    public string Display { get; init; } = LinkDisplay.Selector;
}

public static class LinkDisplay
{
    public const string Selector = "selector";
    public const string Tab = "tab";
    public const string None = "none";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Selector, Tab, None };
}

public static class ManyToManyDetector
{
    /// <summary>
    /// Una tabla puente tiene exactamente dos FK de una columna, su PK son esas dos columnas (declarada o virtual)
    /// y no tiene otras columnas obligatorias: así se puede insertar una fila solo con las dos claves.
    /// </summary>
    public static bool IsBridge(TableInfo t)
    {
        if (t.Kind != TableKind.Table || t.ForeignKeys.Count != 2 || !t.ForeignKeys.All(f => f.IsSingleColumn)) return false;
        var fkCols = t.ForeignKeys.Select(f => f.Columns[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fkCols.Count != 2 || t.PrimaryKey.Count != 2 || !t.PrimaryKey.All(fkCols.Contains)) return false;
        return t.Columns.All(c => fkCols.Contains(c.Name) || !c.IsRequiredOnInsert);
    }

    /// <summary>Relaciones N:M de cada tabla, por clave "esquema\u0001tabla" en minúsculas.</summary>
    public static Dictionary<string, List<ManyToManyLink>> Detect(IReadOnlyList<TableInfo> tables)
    {
        static string Key(string s, string t) => $"{s}\u0001{t}".ToLowerInvariant();
        var byKey = tables.ToDictionary(t => Key(t.Schema, t.Name));
        var result = new Dictionary<string, List<ManyToManyLink>>();

        foreach (var bridge in tables.Where(IsBridge))
        {
            for (var i = 0; i < 2; i++)
            {
                var local = bridge.ForeignKeys[i];
                var other = bridge.ForeignKeys[1 - i];
                // Las dos puntas deben tener PK de una columna: la del padre identifica al registro y la otra da las opciones.
                if (!byKey.TryGetValue(Key(local.RefSchema, local.RefTable), out var owner) || owner.PrimaryKey.Count != 1) continue;
                if (!byKey.TryGetValue(Key(other.RefSchema, other.RefTable), out var target) || target.PrimaryKey.Count != 1) continue;
                var links = result.TryGetValue(Key(owner.Schema, owner.Name), out var l) ? l : result[Key(owner.Schema, owner.Name)] = [];
                links.Add(new ManyToManyLink(
                    bridge.Name, bridge.Schema, bridge.Name, local.Columns[0], other.Columns[0],
                    target.Schema, target.Name, other.RefColumns[0]));
            }
        }

        // Si el mismo puente aparece dos veces en una tabla (N:M consigo misma), el nombre lleva la columna local.
        foreach (var links in result.Values)
            for (var i = 0; i < links.Count; i++)
                if (links.Count(x => x.BridgeSchema == links[i].BridgeSchema && x.BridgeTable == links[i].BridgeTable) > 1)
                    links[i] = links[i] with { Name = $"{links[i].BridgeTable}.{links[i].LocalColumn}" };
        return result;
    }
}
