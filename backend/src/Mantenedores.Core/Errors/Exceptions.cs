namespace Mantenedores.Core.Errors;

public class MantenedorException(string message) : Exception(message);

public sealed class NotFoundException(string message) : MantenedorException(message);

/// <summary>El usuario no tiene permiso para la operación (D-063).</summary>
public sealed class ForbiddenException(string message) : MantenedorException(message);

/// <summary>La operación no está permitida para esta tabla (vista, tabla sin PK, etc.).</summary>
public sealed class OperationNotAllowedException(string message) : MantenedorException(message);

public sealed class ValidationException : MantenedorException
{
    public IReadOnlyDictionary<string, string> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string> errors)
        : base("Hay datos inválidos.") => Errors = errors;

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string> { [field] = error }) { }
}

/// <summary>El servidor está por debajo de la versión mínima soportada (D-035).</summary>
public sealed class UnsupportedServerException(string message) : MantenedorException(message);

/// <summary>
/// El registro cambió desde que el usuario lo leyó (concurrencia optimista, D-069). <c>Current</c> es como está ahora,
/// para que la UI muestre la diferencia y ofrezca recargar o guardar igual.
/// </summary>
public sealed class ConcurrencyException(IDictionary<string, object?> current)
    : MantenedorException("Otra persona cambió este registro desde que lo abrió.")
{
    public IDictionary<string, object?> Current { get; } = current;
}

/// <summary>Demasiadas consultas pesadas a la vez sobre una tabla grande (D-072): reintentar en unos segundos.</summary>
public sealed class BusyException(string message) : MantenedorException(message);

/// <summary>El plan de la licencia no incluye esto (P-116): la UI ofrece ver los planes.</summary>
public sealed class PlanLimitException(string message) : MantenedorException(message);
