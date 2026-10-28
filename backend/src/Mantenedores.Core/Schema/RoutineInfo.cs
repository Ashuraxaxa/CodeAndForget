namespace Mantenedores.Core.Schema;

public enum RoutineKind { Procedure, Function }

/// <summary>Procedimiento almacenado o función del catálogo (P-061). Las acciones de una tabla solo pueden llamar a uno de estos.</summary>
public sealed record RoutineInfo(string Schema, string Name, RoutineKind Kind, IReadOnlyList<RoutineParameter> Parameters)
{
    public string FullName => $"{Schema}.{Name}";
}

/// <summary>Parámetro de entrada de una rutina, en su orden. <c>Type</c> sirve para convertir el valor escrito por el usuario.</summary>
public sealed record RoutineParameter(string Name, int Ordinal, string NativeType, LogicalType Type)
{
    /// <summary>Columna "virtual" para convertir valores con <see cref="Query.ValueConverter"/>.</summary>
    public ColumnInfo AsColumn() => new() { Name = Name, NativeType = NativeType, Type = Type, IsNullable = true };
}

/// <summary>Una fila del catálogo: una rutina con uno de sus parámetros (o sin parámetro, si no tiene).</summary>
/// <param name="RoutineId">Identifica la rutina concreta (oid, object_id…): dos con el mismo nombre son una sobrecarga.</param>
public sealed record RawRoutineParameter(
    string RoutineId, string Schema, string Name, RoutineKind Kind,
    string? ParameterName, int Ordinal, string? NativeType, LogicalType Type, bool IsOutput);

/// <summary>Arma las rutinas desde las filas del catálogo con las mismas reglas en todos los motores (P-061).</summary>
public static class RoutineAssembler
{
    public static IReadOnlyList<RoutineInfo> Assemble(IEnumerable<RawRoutineParameter> rows, IReadOnlyCollection<string>? includeSchemas)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        bool Included(string schema) => includeSchemas is null || includeSchemas.Count == 0 || includeSchemas.Contains(schema, cmp);
        var routines = rows.Where(r => Included(r.Schema)).GroupBy(r => r.RoutineId).ToList();
        // Sobrecargas: mismo esquema y nombre con más de una rutina; no se sabría a cuál llamar.
        var overloaded = routines.GroupBy(g => (g.First().Schema.ToLowerInvariant(), g.First().Name.ToLowerInvariant()))
            .Where(g => g.Count() > 1).SelectMany(g => g.Select(x => x.Key)).ToHashSet();
        var result = new List<RoutineInfo>();
        foreach (var g in routines)
        {
            if (overloaded.Contains(g.Key)) continue;
            var first = g.First();
            var parameters = g.Where(r => r.ParameterName is { Length: > 0 }).OrderBy(r => r.Ordinal).ToList();
            // Un procedimiento con parámetros de salida necesita una variable para recibirlos: no se ofrece.
            if (first.Kind == RoutineKind.Procedure && parameters.Any(p => p.IsOutput)) continue;
            var inputs = parameters.Where(p => !p.IsOutput).Select((p, i) => new RoutineParameter(p.ParameterName!, i + 1, p.NativeType ?? "", p.Type)).ToList();
            // Un parámetro sin nombre (posible en Postgres) no se puede asociar a nada.
            if (g.Any(r => r.ParameterName == "" && !r.IsOutput)) continue;
            result.Add(new RoutineInfo(first.Schema, first.Name, first.Kind, inputs));
        }
        return result.OrderBy(r => r.Schema, cmp).ThenBy(r => r.Name, cmp).ToList();
    }
}
