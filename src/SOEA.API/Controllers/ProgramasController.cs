using Microsoft.AspNetCore.Mvc;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;

namespace SOEA.API.Controllers
{
    public class ProgramaCrudDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public Guid FacultadId { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class ProgramasController : ControllerBase
    {
        private readonly IProgramaRepositorio _repo;

        public ProgramasController(IProgramaRepositorio repo) => _repo = repo;

        [HttpGet]
        public async Task<ActionResult<List<ProgramaCrudDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.OrderBy(p => p.Nombre).Select(MapToDto));
        }

        [HttpPost]
        public async Task<ActionResult<ProgramaCrudDto>> Create([FromBody] ProgramaCrudDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            try
            {
                var programa = new Programa(id, dto.Nombre, dto.FacultadId);
                await _repo.AddAsync(programa);
                return StatusCode(StatusCodes.Status201Created, MapToDto(programa));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ProgramaCrudDto>> Update(Guid id, [FromBody] ProgramaCrudDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound();
            try
            {
                existing.ActualizarDatos(dto.Nombre, dto.FacultadId);
                await _repo.UpdateAsync(existing);
                return Ok(MapToDto(existing));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound($"Programa con ID {id} no encontrado.");
            await _repo.DeleteAsync(id);
            return NoContent();
        }

        private static ProgramaCrudDto MapToDto(Programa p) => new() { Id = p.Id, Nombre = p.Nombre, FacultadId = p.FacultadId };
    }
}
