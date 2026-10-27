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
    Binary,
    Unsupported
}
