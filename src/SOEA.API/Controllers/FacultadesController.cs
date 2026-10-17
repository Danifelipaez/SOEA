using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Facultades;
using SOEA.Domain.Entities;

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
        private readonly FacultadService _service;

        public FacultadesController(FacultadService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<FacultadCrudDto>>> GetAll() =>
            Ok((await _service.ListarAsync()).Select(MapToDto));

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400.
        [HttpPost]
        public async Task<ActionResult<FacultadCrudDto>> Create([FromBody] FacultadCrudDto dto) =>
            StatusCode(StatusCodes.Status201Created, MapToDto(await _service.CrearAsync(dto.Id, dto.Nombre)));

        [HttpPut("{id}")]
        public async Task<ActionResult<FacultadCrudDto>> Update(Guid id, [FromBody] FacultadCrudDto dto) =>
            Ok(MapToDto(await _service.ActualizarAsync(id, dto.Nombre)));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static FacultadCrudDto MapToDto(Facultad f) => new() { Id = f.Id, Nombre = f.Nombre };
    }
}
