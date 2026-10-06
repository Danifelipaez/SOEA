using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SOEA.Domain.Exceptions;

namespace SOEA.API;

/// <summary>
/// ÚNICO traductor de excepción a respuesta HTTP (ERR1/ERR2 auditoría). Antes cada controller
/// además capturaba ArgumentException/InvalidOperationException/KeyNotFoundException por su cuenta
/// — duplicando esta traducción con resultados inconsistentes: el mismo tipo de excepción daba 409
/// en un endpoint y 422 en otro, y "no encontrado" ya se confundía con "conflicto" porque varios
/// servicios de Application lanzaban InvalidOperationException para AMBOS casos. Esos catches se
/// quitaron; todo pasa por aquí.
///
/// InvalidOperationException YA NO se traduce genéricamente a 409: EF Core y el contenedor de DI
/// también la lanzan para fallos internos ("The instance of entity type 'Sesion' cannot be
/// tracked…"), y antes esos mensajes crudos salían igual como si fueran una violación de negocio.
/// Los servicios de Application que necesitan señalar una violación de regla de negocio lanzan
/// <see cref="BusinessRuleViolationException"/> explícitamente; cualquier otra
/// InvalidOperationException cae al ProblemDetails genérico de 500 (registrado en Program.cs), sin
/// exponer su mensaje.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    // Un valor demasiado largo o fuera de rango salía como "ya existe un registro con esos datos" (409),
    // que es falso y manda a buscar un duplicado inexistente. Se distingue por el SQLSTATE de Postgres.
    private static (int, string, string) TraducirDbUpdate(DbUpdateException ex) => (ex.InnerException as PostgresException)?.SqlState switch
    {
        PostgresErrorCodes.StringDataRightTruncation => (StatusCodes.Status400BadRequest, "Solicitud inválida",
            "Un valor es demasiado largo para su campo. Acórtelo e intente de nuevo."),
        PostgresErrorCodes.NumericValueOutOfRange => (StatusCodes.Status400BadRequest, "Solicitud inválida",
            "Un valor numérico está fuera del rango permitido."),
        // Mismo SQLSTATE, sentido opuesto: al BORRAR, la FK Restrict dice que otros registros aún dependen de este.
        PostgresErrorCodes.ForeignKeyViolation when ex.Entries.Any(e => e.State == EntityState.Deleted) =>
            (StatusCodes.Status409Conflict, "Conflicto de datos",
            "No se pudo eliminar: otros registros todavía dependen de este. Elimínelos o cámbielos primero."),
        PostgresErrorCodes.ForeignKeyViolation =>(StatusCodes.Status409Conflict, "Conflicto de datos",
            "No se pudo guardar el cambio: hace referencia a un registro que no existe (o que ya fue eliminado)."),
        PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "Conflicto de datos",
            "No se pudo guardar el cambio: ya existe un registro con esos datos."),
        _ => (StatusCodes.Status409Conflict, "Conflicto de datos",
            "No se pudo guardar el cambio: ya existe un registro con esos datos, o hace referencia a algo que no existe.")
    };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            DbUpdateException dbEx => TraducirDbUpdate(dbEx),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "No encontrado", exception.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, "Solicitud inválida", exception.Message),
            BusinessRuleViolationException => (StatusCodes.Status409Conflict, "Conflicto", exception.Message),
            // L-11: un cuerpo por encima del límite de Kestrel (30 MB) llegaba con el texto técnico en inglés.
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (StatusCodes.Status413PayloadTooLarge,
                "Archivo demasiado grande", "El archivo supera el tamaño máximo permitido (30 MB). Divídalo o reduzca su contenido."),
            _ => (0, "", "")
        };

        if (status == 0) return false;

        _logger.LogWarning(exception, "Excepción traducida a {Status}: {Detail}", status, detail);

        httpContext.Response.StatusCode = status;
        // ERR1 auditoría: el mensaje iba solo en Title; Detail es el campo correcto de ProblemDetails
        // (RFC 7807) para el texto específico de esta instancia, y es el que mensajeErrorHttp() del
        // frontend prioriza. Title queda como una etiqueta corta y estable por tipo de error.
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }
}
