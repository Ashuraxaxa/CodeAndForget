namespace Mantenedores.Core.Schema;

/// <summary>
/// Tipo lógico independiente del motor. Decide qué control dibuja la UI
/// y cómo se convierten los valores que llegan desde la API.
/// </summary>
public enum LogicalType
{
    String,
    Text,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Time,
    Uuid,
    Json,
    Enum,
    /// <summary>Archivo o bytes: no viaja en los listados; se descarga aparte y se sube en base64 (P-018).</summary>
    Binary,

    /// <summary>Arreglo de Postgres (text[], int4[]…): lista de valores de <see cref="ColumnInfo.ElementType"/> (P-018).</summary>
    Array,

    /// <summary>
    /// Valor que el motor sabe mostrar como texto pero no se edita aquí: geography/geometry (WKT), hierarchyid (ruta),
    /// tipos espaciales de MySQL. El dialecto lo convierte a texto al leer (P-018).
    /// </summary>
    ReadOnlyText,

    Unsupported
}
