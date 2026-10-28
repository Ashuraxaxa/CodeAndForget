using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Errors;

public enum DbErrorKind
{
    Unknown,
    Unique,
    ForeignKey,
    Check,
    NotNull,

    /// <summary>El motor usa un mismo código para FK y CHECK (SQL Server 547): se resuelve con la metadata.</summary>
    ForeignKeyOrCheck,

    /// <summary>El usuario de la base no tiene permiso sobre la tabla o la columna (P-073).</summary>
    PermissionDenied
}

/// <summary>
/// Error del motor ya clasificado por su proveedor, a partir de códigos estables
/// (SQLSTATE en Postgres, número de error en SQL Server), no del texto, que cambia con el idioma del servidor.
/// </summary>
public sealed record DbErrorInfo(DbErrorKind Kind, string? Constraint = null, string? Column = null)
{
    public static readonly DbErrorInfo Unknown = new(DbErrorKind.Unknown);
}

/// <summary>La BD rechazó la operación por una restricción; el mensaje ya está en lenguaje del usuario.</summary>
public sealed class ConstraintViolationException(DbErrorKind kind, string message, IReadOnlyDictionary<string, string> errors)
    : MantenedorException(message)
{
    public DbErrorKind Kind { get; } = kind;
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

public enum DbOperation
{
    Insert,
    Update,
    Delete,
    Read
}

/// <summary>
/// Traduce un error clasificado a un mensaje entendible usando la metadata de la tabla (P-003).
/// El nombre de la restricción se busca en la PK, los UNIQUE y las FK de la tabla.
/// Si no se reconoce, devuelve null y se mantiene el mensaje original del motor.
/// </summary>
public static class DbErrorTranslator
{
    public static MantenedorException? Translate(DbErrorInfo info, TableInfo table, DbOperation operation)
    {
        if (info.Kind == DbErrorKind.PermissionDenied)
            return new DbPermissionDeniedException(
                $"El usuario con que la app se conecta a la base no tiene permiso para {Verb(operation)} {table.FullName}. " +
                "Pídale al administrador de la base que le dé ese permiso.");

        var cmp = StringComparer.OrdinalIgnoreCase;
        bool Is(string? name) => info.Constraint is not null && cmp.Equals(info.Constraint, name);

        if (info.Kind == DbErrorKind.ForeignKeyOrCheck)
        {
            var isFk = operation == DbOperation.Delete
                ? table.ReferencedBy.Any(r => Is(r.Name))
                : table.ForeignKeys.Any(f => Is(f.Name));
            info = info with { Kind = isFk ? DbErrorKind.ForeignKey : DbErrorKind.Check };
        }

        switch (info.Kind)
        {
            case DbErrorKind.Unique:
            {
                var columns = Is(table.PrimaryKeyName)
                    ? table.PrimaryKey
                    : table.UniqueKeys.FirstOrDefault(u => Is(u.Name))?.Columns;
                if (columns is null)
                    return new ConstraintViolationException(info.Kind, "Ya existe un registro con esos valores.", Empty);
                return new ConstraintViolationException(info.Kind, $"Ya existe un registro con ese valor en {string.Join(", ", columns)}.",
                    columns.ToDictionary(c => c, _ => "ya existe un registro con este valor"));
            }

            case DbErrorKind.ForeignKey when operation == DbOperation.Delete:
            {
                // Al borrar, la FK que falla es de una tabla hija que referencia a esta.
                var child = table.ReferencedBy.FirstOrDefault(r => Is(r.Name));
                return new ConstraintViolationException(info.Kind, child is null
                    ? "No se puede eliminar: otros registros lo usan."
                    : $"No se puede eliminar: lo usan registros de {child.Table}.", Empty);
            }

            case DbErrorKind.ForeignKey:
            {
                var fk = table.ForeignKeys.FirstOrDefault(f => Is(f.Name));
                if (fk is null) return new ConstraintViolationException(info.Kind, "Uno de los valores no existe en la tabla referenciada.", Empty);
                var cols = string.Join(", ", fk.Columns);
                return new ConstraintViolationException(info.Kind, $"El valor de {cols} no existe en {fk.RefTable}.",
                    fk.Columns.ToDictionary(c => c, _ => $"no existe en {fk.RefTable}"));
            }

            case DbErrorKind.Check:
                return new ConstraintViolationException(info.Kind, info.Constraint is null
                    ? "Un valor no cumple una regla de la base de datos."
                    : $"Un valor no cumple la regla {info.Constraint} de la base de datos.", Empty);

            case DbErrorKind.NotNull:
            {
                var col = info.Column is null ? null : table.FindColumn(info.Column)?.Name ?? info.Column;
                return col is null
                    ? new ConstraintViolationException(info.Kind, "Falta un valor obligatorio.", Empty)
                    : new ConstraintViolationException(info.Kind, $"{col} es obligatorio.", new Dictionary<string, string> { [col] = "es obligatorio" });
            }

            default:
                return null;
        }
    }

    private static string Verb(DbOperation op) => op switch
    {
        DbOperation.Insert => "crear registros en",
        DbOperation.Update => "editar registros de",
        DbOperation.Delete => "borrar registros de",
        _ => "leer"
    };

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
}

/// <summary>La base rechazó la operación por permisos de su propio usuario (P-073), no por los de la app.</summary>
public sealed class DbPermissionDeniedException(string message) : MantenedorException(message);
