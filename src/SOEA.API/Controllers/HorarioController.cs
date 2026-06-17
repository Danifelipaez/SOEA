using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HorarioController : ControllerBase
    {
        private readonly GenerarHorarioService       _generarService;
        private readonly CrearSesionManualService    _sesionManualService;
        private readonly ILogger<HorarioController>  _logger;

        public HorarioController(
            GenerarHorarioService       generarService,
            CrearSesionManualService    sesionManualService,
            ILogger<HorarioController>  logger)
        {
            _generarService      = generarService;
            _sesionManualService = sesionManualService;
            _logger              = logger;
        }

        /// <summary>
        /// Genera un horario académico ejecutando el pipeline de 3 fases
        /// (GraphColoring → CP-SAT → Genetic Algorithm).
        /// Recibe el estado actual del frontend (asignaturas, docentes, espacios)
        /// y devuelve las sesiones programadas listas para pintar en la matriz.
        /// </summary>
        [HttpPost("generar")]
        [ProducesResponseType(typeof(GenerarHorarioResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(GenerarHorarioResponse), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GenerarHorario([FromBody] GenerarHorarioRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (request.Asignaturas.Count == 0)
                return BadRequest("Debe incluir al menos una asignatura en el request.");

            if (request.Docentes.Count == 0)
                return BadRequest("Debe incluir al menos un docente en el request.");

            if (request.Espacios.Count == 0)
                return BadRequest("Debe incluir al menos un espacio en el request.");

            try
            {
                _logger.LogInformation(
                    "Iniciando generación de horario para semestre {Semestre} con {NumAsig} asignaturas, {NumDoc} docentes, {NumEsp} espacios.",
                    request.Semestre,
                    request.Asignaturas.Count,
                    request.Docentes.Count,
                    request.Espacios.Count);

                var resultado = await _generarService.EjecutarAsync(request, ct);

                if (!resultado.EsFactible)
                {
                    _logger.LogWarning(
                        "No se encontró solución factible para semestre {Semestre}: {Error}",
                        request.Semestre, resultado.MensajeError);

                    return UnprocessableEntity(resultado);
                }

                _logger.LogInformation(
                    "Horario generado exitosamente. Id={HorarioId}, Sesiones={N}, Fitness={F}",
                    resultado.HorarioId, resultado.Sesiones.Count, resultado.PuntajeFitness);

                return Ok(resultado);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Generación de horario cancelada por desconexión del cliente.");
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al generar el horario.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Error interno al generar el horario.", detalle = ex.Message });
            }
        }

        /// <summary>
        /// Crea una sesión manualmente sin re-ejecutar el modelo de optimización.
        /// Valida HC-I01, HC-S01 y HC-S05 antes de persistir.
        /// Devuelve los DTOs de la sesión creada (1 ó 2 filas: semana A + semana B).
        /// </summary>
        [HttpPost("sesion-manual")]
        [ProducesResponseType(typeof(List<SesionGeneradaDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> CrearSesionManual([FromBody] CrearSesionManualRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var resultado = await _sesionManualService.EjecutarAsync(request);
                return StatusCode(StatusCodes.Status201Created, resultado);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // Violación de hard constraint — el mensaje ya viene en español para el usuario
                return UnprocessableEntity(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al crear sesión manual.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Error interno al crear la sesión.", detalle = ex.Message });
            }
        }
    }
}
