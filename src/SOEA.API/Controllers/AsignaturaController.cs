using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AsignaturasController : ControllerBase
    {
        private readonly AsignaturaService _service;

        public AsignaturasController(AsignaturaService service) => _service = service;

        [HttpPost]
        public async Task<ActionResult<AsignaturaResponse>> CreateAsignatura(
            [FromBody] CreateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var response = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(GetAsignatura), new { id = response.Id }, response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> GetAsignatura(Guid id)
        {
            try
            {
                var response = await _service.GetByIdAsync(id);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<AsignaturaResponse>>> GetAllAsignaturas()
        {
            // Bug (auditoría de limpieza, hallazgo 1.8): catch-all que filtraba ex.Message crudo
            // al cliente en un 500 — cualquier excepción no relacionada con la petición (un fallo
            // de conexión a BD, por ejemplo) exponía detalle interno en vez del ProblemDetails
            // genérico. GlobalExceptionHandler ya cubre esto; el resto de este controller (y
            // HorarioController/ImportController) no lleva catch-all a propósito, por la misma razón.
            var responses = await _service.GetAllAsync();
            return Ok(responses);
        }

        /// <summary>
        /// Actualiza los datos editables de una asignatura (nombre, código, duración,
        /// programa, docente y espacio fijo) desde la UI de Ingesta.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> UpdateAsignatura(
            Guid id, [FromBody] UpdateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var response = await _service.UpdateAsync(id, request);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsignatura(Guid id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }

        /// <summary>
        /// Marca (o desmarca) la asignatura como candidata a ceder a alternancia si el algoritmo
        /// agota el espacio físico disponible (cesión por saturación de espacio).
        /// Body: { "elegible": true | false }
        /// </summary>
        [HttpPatch("{id}/elegibilidad-alternancia")]
        public async Task<IActionResult> UpdateElegibilidadAlternancia(Guid id, [FromBody] UpdateElegibilidadAlternanciaDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _service.UpdateElegibilidadAlternanciaAsync(id, dto.Elegible);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }

    public class UpdateElegibilidadAlternanciaDto
    {
        public bool Elegible { get; set; }
    }
}
