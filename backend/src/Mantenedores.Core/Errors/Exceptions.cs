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
