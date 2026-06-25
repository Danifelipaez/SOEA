using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;
using SOEA.Domain.Enums;

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
            try
            {
                var responses = await _service.GetAllAsync();
                return Ok(responses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
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
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        /// Actualiza el tipo de alternancia de una asignatura.
        /// Body: { "alternancia": "TipoA" | "TipoB" | "SinAlternancia" }
        /// </summary>
        [HttpPatch("{id}/alternancia")]
        public async Task<IActionResult> UpdateAlternancia(Guid id, [FromBody] UpdateAlternanciaDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _service.UpdateAlternanciaAsync(id, dto.Alternancia);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }

    public class UpdateAlternanciaDto
    {
        public TipoAlternancia Alternancia { get; set; }
    }
}
