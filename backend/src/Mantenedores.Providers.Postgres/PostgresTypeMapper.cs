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
        if (IsTextCast(typeName, typeKind)) return (LogicalType.String, false);
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
            // PostGIS (P-018, P-104): como WKT, editable.
            "geometry" or "geography" => (LogicalType.Spatial, false),
            _ => (LogicalType.Unsupported, false)
        };
    }

    /// <summary>
    /// SRID de una columna PostGIS a partir de <c>format_type</c>: "geometry(Point,4326)" → 4326. Sin SRID en el tipo:
    /// geography usa 4326 (lo que PostGIS supone) y geometry 0.
    /// </summary>
    public static int? Srid(string typeName, string? formatType)
    {
        if (typeName is not ("geometry" or "geography")) return null;
        var m = System.Text.RegularExpressions.Regex.Match(formatType ?? "", @",\s*(\d+)\s*\)");
        return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : typeName == "geography" ? 4326 : 0;
    }

    /// <summary>
    /// Tipos que Postgres convierte desde y hacia texto sin pérdida y que se editan como texto (P-075): compuestos (c),
    /// rangos (r), multirangos (m, PG 14+) y algunos tipos base (redes, intervalos, bits, geometría simple).
    /// </summary>
    public static bool IsTextCast(string typeName, string typeKind) =>
        typeKind is "c" or "r" or "m" ||
        typeName is "interval" or "inet" or "cidr" or "macaddr" or "macaddr8" or "bit" or "varbit"
            or "point" or "line" or "lseg" or "box" or "path" or "polygon" or "circle" or "timetz" or "pg_lsn";

    /// <summary>Tipo del elemento de un arreglo; si no se puede editar como lista, el arreglo queda sin soporte.</summary>
    public static LogicalType? MapElement(string? typeName, string? typeKind, string? typeCategory)
    {
        if (typeName is null) return null;
        var (t, _) = Map(typeName, typeKind ?? "b", typeCategory ?? "");
        return t is LogicalType.Array or LogicalType.Binary or LogicalType.Json or LogicalType.ReadOnlyText or LogicalType.Spatial or LogicalType.Unsupported
            ? null
            : t;
    }
}
