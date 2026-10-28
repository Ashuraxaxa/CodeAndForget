using System.Data.Common;
using Mantenedores.Core.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace Mantenedores.Api.Infrastructure;

/// <summary>Traduce las excepciones a respuestas ProblemDetails con mensajes entendibles.</summary>
public sealed class ErrorHandler(ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        IResult result = ex switch
        {
            ValidationException v => Results.ValidationProblem(
                v.Errors.ToDictionary(e => e.Key, e => new[] { e.Value }), title: v.Message),
            BusyException b => Busy(http, b),
            ForbiddenException f => Results.Problem(f.Message, statusCode: 403, title: "Sin permiso"),
            // Concurrencia optimista (D-069): se devuelve cómo está ahora, para comparar y decidir.
            ConcurrencyException c => Results.Problem(c.Message, statusCode: 409, title: "El registro cambió",
                type: "mantenedores:concurrencia", extensions: new Dictionary<string, object?> { ["current"] = c.Current }),
            NotFoundException n => Results.Problem(n.Message, statusCode: 404, title: "No encontrado"),
            // Restricción de la BD ya traducida (P-003): mensaje claro y, si se puede, el campo afectado.
            ConstraintViolationException c => Results.ValidationProblem(
                c.Errors.ToDictionary(e => e.Key, e => new[] { e.Value }),
                detail: c.Message, statusCode: 409, title: "La base de datos rechazó la operación"),
            OperationNotAllowedException o => Results.Problem(o.Message, statusCode: 400, title: "Operación no permitida"),
            UnsupportedServerException u => Results.Problem(u.Message, statusCode: 400, title: "Versión de base de datos no soportada"),
            NotSupportedException s => Results.Problem(s.Message, statusCode: 400, title: "No soportado"),
            // Errores del motor (FK, únicos, CHECK, triggers): se muestran porque el usuario necesita saber qué pasó.
            DbException db => LogAndReturn(db, Results.Problem(db.Message, statusCode: 409, title: "La base de datos rechazó la operación")),
            OperationCanceledException => Results.StatusCode(499),
            _ => LogAndReturn(ex, Results.Problem("Ocurrió un error inesperado.", statusCode: 500, title: "Error interno"))
        };

        await result.ExecuteAsync(http);
        return true;
    }

    private static IResult Busy(HttpContext http, BusyException b)
    {
        http.Response.Headers.RetryAfter = "5";
        return Results.Problem(b.Message, statusCode: 503, title: "Ocupado");
    }

    private IResult LogAndReturn(Exception ex, IResult result)
    {
        logger.LogError(ex, "Error procesando la solicitud");
        return result;
    }
}
