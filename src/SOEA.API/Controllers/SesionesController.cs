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
        private readonly ILogger<SesionesController> _logger;

        public SesionesController(
            AsignarDocenteSesionService asignarService,
            ILogger<SesionesController> logger)
        {
            _asignarService = asignarService;
            _logger         = logger;
        }

        /// <summary>
        /// Asigna o desasigna un docente a una sesión ya generada.
        /// Presencial-first (CR-02/CR-08): el docente no participa en la generación;
        /// se asigna aquí después. Enviar { "docenteId": null } para desasignar.
        /// </summary>
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

            try
            {
                var resultado = await _asignarService.EjecutarAsync(request);
                return Ok(resultado);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // HC-I01 (edición): solape de franja del docente — hard constraint.
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al asignar docente a sesión {Id}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Error interno al asignar el docente.", detalle = ex.Message });
            }
        }
    }
}
