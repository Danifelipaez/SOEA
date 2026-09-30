using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Facultades;
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
        private readonly FacultadService _service;

        public FacultadesController(IFacultadRepositorio repo, FacultadService service)
        {
            _repo = repo;
            _service = service;
        }

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
            await _service.ExigirNombreUnicoAsync(dto.Nombre, id);
            var facultad = new Facultad(id, dto.Nombre);
            await _repo.AddAsync(facultad);
            return StatusCode(StatusCodes.Status201Created, MapToDto(facultad));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<FacultadCrudDto>> Update(Guid id, [FromBody] FacultadCrudDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) throw new KeyNotFoundException($"Facultad con ID {id} no encontrada.");
            await _service.ExigirNombreUnicoAsync(dto.Nombre, id);
            existing.ActualizarNombre(dto.Nombre);
            await _repo.UpdateAsync(existing);
            return Ok(MapToDto(existing));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static FacultadCrudDto MapToDto(Facultad f) => new() { Id = f.Id, Nombre = f.Nombre };
    }
}
