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
        if (typeCategory == "A") return (LogicalType.Unsupported, false); // arreglos: Fase 2

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
            _ => (LogicalType.Unsupported, false)
        };
    }
}
