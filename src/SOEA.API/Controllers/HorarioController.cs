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
        private readonly ReacomodarHorarioService    _reacomodarService;
        private readonly ILogger<HorarioController>  _logger;

        public HorarioController(
            GenerarHorarioService       generarService,
            CrearSesionManualService    sesionManualService,
            ReacomodarHorarioService    reacomodarService,
            ILogger<HorarioController>  logger)
        {
            _generarService      = generarService;
            _sesionManualService = sesionManualService;
            _reacomodarService   = reacomodarService;
            _logger              = logger;
        }

        /// <summary>
        /// Recupera el horario vigente ya persistido para un semestre (última corrida generada),
        /// para que el frontend pueda rehidratar la grilla tras un reload sin volver a ejecutar el
        /// pipeline. 404 si aún no se ha generado ningún horario para ese semestre.
        /// </summary>
        [HttpGet("actual")]
        [ProducesResponseType(typeof(GenerarHorarioResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ObtenerActual([FromQuery] string semestre)
        {
            if (string.IsNullOrWhiteSpace(semestre))
                return BadRequest("Debe especificar el semestre.");

            var resultado = await _generarService.ObtenerActualAsync(semestre);
            return resultado is null ? NotFound() : Ok(resultado);
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

            // M1 auditoría: CR-08 (presencial-first) sacó al docente del pipeline de generación —
            // solo alimenta objetivos blandos del algoritmo genético, y se asigna después vía
            // PATCH /api/sesiones/{id}/docente (SesionesController). Exigir al menos un docente
            // aquí contradecía esa arquitectura y bloqueaba el flujo normal: cargar asignaturas y
            // espacios, generar, y solo entonces asignar docentes.

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
            // B3 auditoría: sin catch-all aquí — cualquier otra excepción cae en GlobalExceptionHandler
            // (A1), que ya no expone ex.Message crudo para lo que no reconoce.
        }

        /// <summary>
        /// Crea una sesión manualmente sin re-ejecutar el modelo de optimización.
        /// Valida HC-I01, HC-S01 y HC-S05 antes de persistir.
        /// Devuelve los DTOs de la sesión creada (1 ó 2 filas: semana A + semana B).
        /// </summary>
        // ERR1/ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400
        // y BusinessRuleViolationException (violación de HC-I01/HC-S01/HC-S05/HC-SEP) a 409. Antes
        // este endpoint era el único que devolvía 422 para una violación de restricción dura —
        // SesionesController y el resto del backend ya usaban 409 para el mismo caso.
        [HttpPost("sesion-manual")]
        [ProducesResponseType(typeof(List<SesionGeneradaDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CrearSesionManual([FromBody] CrearSesionManualRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var resultado = await _sesionManualService.EjecutarAsync(request);
            return StatusCode(StatusCodes.Status201Created, resultado);
        }

        /// <summary>
        /// Petición 13 — mueve una sesión ya generada a un nuevo (día, hora, espacio) sin
        /// regenerar el horario completo. Solo la sesión editada y las que ahora chocan con ella
        /// se recalculan; el resto del horario no se mueve.
        /// </summary>
        [HttpPost("reacomodar")]
        [ProducesResponseType(typeof(ReacomodarHorarioResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ReacomodarHorarioResponse), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce KeyNotFoundException a 404 y
        // ArgumentException a 400.
        public async Task<IActionResult> Reacomodar([FromBody] ReacomodarHorarioRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var resultado = await _reacomodarService.EjecutarAsync(request, ct);
            return resultado.EsFactible ? Ok(resultado) : UnprocessableEntity(resultado);
        }
    }
}
