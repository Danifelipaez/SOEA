using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Programas;
using SOEA.Domain.Entities;

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
        private readonly ProgramaService _service;

        public ProgramasController(ProgramaService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<ProgramaCrudDto>>> GetAll() =>
            Ok((await _service.ListarAsync()).Select(MapToDto));

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400.
        [HttpPost]
        public async Task<ActionResult<ProgramaCrudDto>> Create([FromBody] ProgramaCrudDto dto) =>
            StatusCode(StatusCodes.Status201Created, MapToDto(await _service.CrearAsync(dto.Id, dto.Nombre, dto.FacultadId)));

        [HttpPut("{id}")]
        public async Task<ActionResult<ProgramaCrudDto>> Update(Guid id, [FromBody] ProgramaCrudDto dto) =>
            Ok(MapToDto(await _service.ActualizarAsync(id, dto.Nombre, dto.FacultadId)));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static ProgramaCrudDto MapToDto(Programa p) => new() { Id = p.Id, Nombre = p.Nombre, FacultadId = p.FacultadId };
    }
}
