using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Programas;
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
        private readonly ProgramaService _service;

        public ProgramasController(IProgramaRepositorio repo, ProgramaService service)
        {
            _repo = repo;
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<ProgramaCrudDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.OrderBy(p => p.Nombre).Select(MapToDto));
        }

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400.
        [HttpPost]
        public async Task<ActionResult<ProgramaCrudDto>> Create([FromBody] ProgramaCrudDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            await _service.ExigirNombreUnicoAsync(dto.Nombre, dto.FacultadId, id);
            var programa = new Programa(id, dto.Nombre, dto.FacultadId);
            await _repo.AddAsync(programa);
            return StatusCode(StatusCodes.Status201Created, MapToDto(programa));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ProgramaCrudDto>> Update(Guid id, [FromBody] ProgramaCrudDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) throw new KeyNotFoundException($"Programa con ID {id} no encontrado.");
            await _service.ExigirNombreUnicoAsync(dto.Nombre, dto.FacultadId, id);
            existing.ActualizarDatos(dto.Nombre, dto.FacultadId);
            await _repo.UpdateAsync(existing);
            return Ok(MapToDto(existing));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static ProgramaCrudDto MapToDto(Programa p) => new() { Id = p.Id, Nombre = p.Nombre, FacultadId = p.FacultadId };
    }
}
