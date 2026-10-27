using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.Postgres;

public static class PostgresTypeMapper
{
    /// <param name="typeName">Nombre interno del tipo base (pg_type.typname).</param>
    /// <param name="typeKind">pg_type.typtype: b=base, e=enum, d=dominio...</param>
    /// <param name="typeCategory">pg_type.typcategory: A=arreglo...</param>
    public static (LogicalType Type, bool HasTimeZone) Map(string typeName, string typeKind, string typeCategory)
    {
        if (typeKind == "e") return (LogicalType.Enum, false);
        if (typeCategory == "A") return (LogicalType.Array, false); // el elemento se resuelve con MapElement (P-018)

        return typeName switch
        {
            "int2" or "int4" or "int8" => (LogicalType.Integer, false),
            "numeric" or "float4" or "float8" or "money" => (LogicalType.Decimal, false),
            "bool" => (LogicalType.Boolean, false),
            "varchar" or "bpchar" or "char" or "name" or "citext" => (LogicalType.String, false),
            "text" or "xml" => (LogicalType.Text, false),
            "date" => (LogicalType.Date, false),
            "timestamp" => (LogicalType.DateTime, false),
            "timestamptz" => (LogicalType.DateTime, true),
            "time" => (LogicalType.Time, false),
            "uuid" => (LogicalType.Uuid, false),
            "json" or "jsonb" => (LogicalType.Json, false),
            "bytea" => (LogicalType.Binary, false),
            // PostGIS (P-018): como WKT, de solo lectura.
            "geometry" or "geography" => (LogicalType.ReadOnlyText, false),
            _ => (LogicalType.Unsupported, false)
        };
    }

    /// <summary>Tipo del elemento de un arreglo; si no se puede editar como lista, el arreglo queda sin soporte.</summary>
    public static LogicalType? MapElement(string? typeName, string? typeKind, string? typeCategory)
    {
        if (typeName is null) return null;
        var (t, _) = Map(typeName, typeKind ?? "b", typeCategory ?? "");
        return t is LogicalType.Array or LogicalType.Binary or LogicalType.Json or LogicalType.ReadOnlyText or LogicalType.Unsupported
            ? null
            : t;
    }
}
