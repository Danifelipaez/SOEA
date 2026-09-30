using Microsoft.AspNetCore.Mvc;

namespace SOEA.API;

/// <summary>
/// Respuesta de validación de modelo en español y con el mismo formato que el resto de errores (L-11
/// auditoría 2026-09-28). Sin esto, <c>[ApiController]</c> devolvía los mensajes por defecto de ASP.NET en inglés
/// ("The semestre field is required", "The JSON value could not be converted…") y, al subir un archivo más grande
/// que el límite, "Failed to read the request form. Request body too large. The max request body size is
/// 30000000 bytes." — texto que la coordinadora no puede accionar.
/// </summary>
public static class ValidacionModelo
{
    /// <summary>Tamaño máximo por defecto del cuerpo de una petición en Kestrel (30 000 000 bytes ≈ 30 MB).</summary>
    private const string TamañoMaximo = "30 MB";

    public static IActionResult RespuestaEnEspanol(ActionContext contexto)
    {
        var conError = contexto.ModelState.Where(e => e.Value?.Errors.Count > 0).ToList();

        var cuerpoGrande = conError
            .SelectMany(e => e.Value!.Errors)
            .Any(er => (er.ErrorMessage + er.Exception?.Message).Contains("too large", StringComparison.OrdinalIgnoreCase));
        if (cuerpoGrande)
            return Resultado(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Archivo demasiado grande",
                Detail = $"El archivo supera el tamaño máximo permitido ({TamañoMaximo}). Divídalo o reduzca su contenido."
            });

        // La clave de un error de JSON viene como "$.configuracion.maxGeneraciones"; la de un error del cuerpo, vacía.
        var campos = conError
            .Select(e => e.Key.StartsWith("$.") ? e.Key[2..] : e.Key)
            .Select(k => string.IsNullOrWhiteSpace(k) ? "solicitud" : k)
            .Distinct()
            .ToList();

        var errores = campos.ToDictionary(c => c, c => new[] { $"El campo '{c}' es obligatorio o no tiene un valor válido." });
        return Resultado(new ValidationProblemDetails(errores)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Datos inválidos",
            Detail = campos.Count > 0
                ? $"Revise los campos: {string.Join(", ", campos)}."
                : "La solicitud no es válida."
        });
    }

    /// <summary>
    /// Títulos en español para los errores que <c>[ApiController]</c> genera sin pasar por el código
    /// (p. ej. 415 al enviar un cuerpo sin <c>Content-Type: application/json</c>, que salía como
    /// "Unsupported Media Type").
    /// </summary>
    public static void TraducirErroresCliente(ApiBehaviorOptions o)
    {
        var titulos = new Dictionary<int, string>
        {
            [400] = "Solicitud inválida",
            [401] = "No autorizado",
            [403] = "Acceso denegado",
            [404] = "No encontrado",
            [405] = "Operación no permitida",
            [406] = "Formato de respuesta no disponible",
            [408] = "La solicitud tardó demasiado",
            [409] = "Conflicto",
            [412] = "Condición previa no cumplida",
            [415] = "Formato de datos no admitido: envíe el contenido como JSON (o el archivo como formulario).",
            [422] = "Datos no procesables",
            [500] = "Error interno del servidor",
        };
        foreach (var (codigo, titulo) in titulos)
            o.ClientErrorMapping[codigo] = new ClientErrorData { Title = titulo, Link = o.ClientErrorMapping.TryGetValue(codigo, out var d) ? d.Link : null };
    }

    private static ObjectResult Resultado(ProblemDetails problema) =>
        new(problema) { StatusCode = problema.Status, ContentTypes = { "application/problem+json" } };
}
