using Microsoft.AspNetCore.Mvc;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;

namespace SOEA.API.Controllers
{
    public class FacultadCrudDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
    }

    [ApiController]
    [Route("api/[controller]")]
    public class FacultadesController : ControllerBase
    {
        private readonly IFacultadRepositorio _repo;

        public FacultadesController(IFacultadRepositorio repo) => _repo = repo;

        [HttpGet]
        public async Task<ActionResult<List<FacultadCrudDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.OrderBy(f => f.Nombre).Select(MapToDto));
        }

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400.
        [HttpPost]
        public async Task<ActionResult<FacultadCrudDto>> Create([FromBody] FacultadCrudDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            var facultad = new Facultad(id, dto.Nombre);
            await _repo.AddAsync(facultad);
            return StatusCode(StatusCodes.Status201Created, MapToDto(facultad));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<FacultadCrudDto>> Update(Guid id, [FromBody] FacultadCrudDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.ActualizarNombre(dto.Nombre);
            await _repo.UpdateAsync(existing);
            return Ok(MapToDto(existing));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound($"Facultad con ID {id} no encontrada.");
            await _repo.DeleteAsync(id);
            return NoContent();
        }

        private static FacultadCrudDto MapToDto(Facultad f) => new() { Id = f.Id, Nombre = f.Nombre };
    }
}
