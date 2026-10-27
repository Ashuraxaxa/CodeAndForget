namespace Mantenedores.Core.Schema;

/// <summary>
/// Elige qué columna representa a un registro cuando otra tabla lo referencia.
/// Primero busca nombres típicos; si no hay, la primera columna de texto corto que no sea clave; si no, la PK.
/// </summary>
public static class DisplayColumnResolver
{
    private static readonly string[] Preferred =
    [
        "nombre", "name", "descripcion", "description", "titulo", "title",
        "razon_social", "razonsocial", "glosa", "detalle", "label", "codigo", "code"
    ];

    public static string? Resolve(IReadOnlyList<ColumnInfo> columns, IReadOnlyCollection<string> fkColumns)
    {
        var candidates = columns.Where(c => c.IsSelectable).ToList();
        if (candidates.Count == 0) return null;

        bool IsText(ColumnInfo c) => c.Type is LogicalType.String or LogicalType.Text;
        string Normalize(string s) => s.Replace("_", "").ToLowerInvariant();

        foreach (var name in Preferred)
        {
            var exact = candidates.FirstOrDefault(c => IsText(c) && Normalize(c.Name) == Normalize(name));
            if (exact is not null) return exact.Name;
        }

        foreach (var name in Preferred)
        {
            var partial = candidates.FirstOrDefault(c => IsText(c) && Normalize(c.Name).Contains(Normalize(name)));
            if (partial is not null) return partial.Name;
        }

        bool IsKey(ColumnInfo c) => c.IsPrimaryKey || fkColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase);

        // Un texto largo (observaciones, notas) no sirve como etiqueta: en ese caso se usa la PK.
        return candidates.FirstOrDefault(c => c.Type == LogicalType.String && !IsKey(c))?.Name
            ?? candidates.FirstOrDefault(c => c.IsPrimaryKey)?.Name
            ?? candidates.FirstOrDefault(c => IsText(c) && !IsKey(c))?.Name
            ?? candidates[0].Name;
    }
}
