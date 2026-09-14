using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Docentes;

namespace SOEA.API.Controllers
{
    // ── Controller ────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/[controller]")]
    public class DocentesController : ControllerBase
    {
        private readonly DocenteService _service;
        private readonly FusionDocentesService _fusion;

        public DocentesController(DocenteService service, FusionDocentesService fusion)
        {
            _service = service;
            _fusion  = fusion;
        }

        [HttpGet]
        public async Task<ActionResult<List<DocenteUiDto>>> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(list);
        }

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400 y
        // BusinessRuleViolationException a 409 (DeleteAsync ya no lanza InvalidOperationException).
        [HttpPost]
        public async Task<ActionResult<DocenteUiDto>> Create([FromBody] DocenteUiDto dto)
        {
            var created = await _service.CreateAsync(dto);
            return StatusCode(StatusCodes.Status201Created, created);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<DocenteUiDto>> Update(Guid id, [FromBody] DocenteUiDto dto)
        {
            var updated = await _service.UpdateAsync(id, dto);
            if (updated is null) return NotFound();
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var eliminado = await _service.DeleteAsync(id);
            if (!eliminado) return NotFound($"Docente con ID {id} no encontrado.");
            return NoContent();
        }

        /// <summary>
        /// Grupos de docentes que probablemente son la misma persona (variantes de nombre),
        /// para revisión y fusión manual desde la UI.
        /// </summary>
        [HttpGet("duplicados")]
        public async Task<ActionResult<List<List<DocenteUiDto>>>> GetDuplicados()
            => Ok(await _fusion.SugerirDuplicadosAsync());

        /// <summary>
        /// Fusiona los docentes duplicados en el canónico: reasigna sus asignaturas y los elimina.
        /// </summary>
        [HttpPost("fusionar")]
        public async Task<IActionResult> Fusionar([FromBody] FusionarDocentesRequest req)
        {
            var resultado = await _fusion.FusionarAsync(req.CanonicoId, req.DuplicadosIds ?? new List<Guid>());
            return Ok(resultado);
        }
    }

    /// <summary>Payload para fusionar docentes: el canónico que se conserva y los que se absorben.</summary>
    public record FusionarDocentesRequest(Guid CanonicoId, List<Guid> DuplicadosIds);
}
