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

        public DocentesController(DocenteService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<DocenteUiDto>>> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<ActionResult<DocenteUiDto>> Create([FromBody] DocenteUiDto dto)
        {
            try
            {
                var created = await _service.CreateAsync(dto);
                return StatusCode(StatusCodes.Status201Created, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<DocenteUiDto>> Update(Guid id, [FromBody] DocenteUiDto dto)
        {
            try
            {
                var updated = await _service.UpdateAsync(id, dto);
                if (updated is null) return NotFound();
                return Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }
    }
}
