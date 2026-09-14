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

        // ERR2 auditoría: sin catch — GlobalExceptionHandler es el único traductor de excepción a
        // HTTP (ArgumentException→400, KeyNotFoundException→404, BusinessRuleViolationException→409).
        // Antes este controller y AsignaturaService no siempre usaban el mismo tipo para "no
        // encontrada" (aquí InvalidOperationException→404, en otro método KeyNotFoundException→404
        // en Delete), así que "asignatura no encontrada" era 404 o 409 según el endpoint.

        [HttpPost]
        public async Task<ActionResult<AsignaturaResponse>> CreateAsignatura(
            [FromBody] CreateAsignaturaRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(GetAsignatura), new { id = response.Id }, response);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AsignaturaResponse>> GetAsignatura(Guid id)
        {
            var response = await _service.GetByIdAsync(id);
            return Ok(response);
        }

        [HttpGet]
        public async Task<ActionResult<List<AsignaturaResponse>>> GetAllAsignaturas()
        {
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

            var response = await _service.UpdateAsync(id, request);
            return Ok(response);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsignatura(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
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
            await _service.UpdateElegibilidadAlternanciaAsync(id, dto.Elegible);
            return NoContent();
        }
    }

    public class UpdateElegibilidadAlternanciaDto
    {
        public bool Elegible { get; set; }
    }
}
