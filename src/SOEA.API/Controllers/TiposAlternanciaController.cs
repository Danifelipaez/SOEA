using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.TiposAlternancia;

namespace SOEA.API.Controllers
{
    /// <summary>
    /// Catálogo editable de tipos de alternancia (Inc. C): permite editar TipoA/TipoB y agregar
    /// más tipos sobre la base de 2 semanas (A/B).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TiposAlternanciaController : ControllerBase
    {
        private readonly TipoAlternanciaConfigService _service;

        public TiposAlternanciaController(TipoAlternanciaConfigService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<TipoAlternanciaConfigDto>>> GetAll()
            => Ok(await _service.GetAllAsync());

        [HttpPost]
        public async Task<ActionResult<TipoAlternanciaConfigDto>> Create([FromBody] TipoAlternanciaConfigDto dto)
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
        public async Task<ActionResult<TipoAlternanciaConfigDto>> Update(Guid id, [FromBody] TipoAlternanciaConfigDto dto)
        {
            try
            {
                var updated = await _service.UpdateAsync(id, dto);
                return updated is null ? NotFound() : Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                var ok = await _service.DeleteAsync(id);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
