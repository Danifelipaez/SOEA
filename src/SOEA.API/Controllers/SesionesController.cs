using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/sesiones")]
    public class SesionesController : ControllerBase
    {
        private readonly AsignarDocenteSesionService _asignarService;

        public SesionesController(AsignarDocenteSesionService asignarService)
        {
            _asignarService = asignarService;
        }

        /// <summary>
        /// Asigna o desasigna un docente a una sesión ya generada.
        /// Presencial-first (CR-02/CR-08): el docente no participa en la generación;
        /// se asigna aquí después. Enviar { "docenteId": null } para desasignar.
        /// </summary>
        // ERR1/ERR2 auditoría: sin catch — GlobalExceptionHandler es el único traductor
        // (KeyNotFoundException→404, ArgumentException→400, BusinessRuleViolationException→409 para
        // HC-I01). El catch-all que había aquí antes ya no exponía ex.Message crudo, pero seguía
        // duplicando la traducción genérica de GlobalExceptionHandler con su propia forma de
        // respuesta — cualquier 500 no reconocido en el resto de la API ahora responde igual.
        [HttpPatch("{id}/docente")]
        [ProducesResponseType(typeof(AsignarDocenteResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AsignarDocente(
            Guid id,
            [FromBody] AsignarDocenteRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            request.SesionId = id;
            var resultado = await _asignarService.EjecutarAsync(request);
            return Ok(resultado);
        }
    }
}
