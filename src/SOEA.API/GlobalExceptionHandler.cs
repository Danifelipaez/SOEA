using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SOEA.API;

/// <summary>
/// Red de seguridad para excepciones que ningún controller capturó puntualmente: sin esto,
/// llegaban al 500 genérico de ASP.NET Core (solo traceId, sin causa) — el coordinador académico
/// que opera SOEA no tiene a quién preguntarle qué significa eso. Traduce las mismas excepciones
/// que los controllers ya capturan caso por caso (ArgumentException, InvalidOperationException,
/// KeyNotFoundException) más DbUpdateException (violaciones de índice único en EF Core, que
/// varios controllers nunca capturaban, p. ej. Delete en Facultades/Programas/Espacios/Docentes).
/// Cualquier otra excepción cae al ProblemDetails genérico registrado en Program.cs.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = exception switch
        {
            DbUpdateException => (StatusCodes.Status409Conflict,
                "No se pudo guardar el cambio: ya existe un registro con esos datos, o hace referencia a algo que no existe."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),
            InvalidOperationException => (StatusCodes.Status409Conflict, exception.Message),
            _ => (0, "")
        };

        if (status == 0) return false;

        _logger.LogWarning(exception, "Excepción traducida a {Status}: {Detail}", status, detail);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = detail
        }, cancellationToken);

        return true;
    }
}
