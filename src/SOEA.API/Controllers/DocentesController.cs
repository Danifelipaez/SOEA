using Microsoft.AspNetCore.Mvc;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using System.Text.Json;

namespace SOEA.API.Controllers
{
    // ── DTOs ──────────────────────────────────────────────────────────────────────

    public class DocenteDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Cedula { get; set; } = "";
        public double MaxHoras { get; set; }
        public JsonElement? Disponibilidad { get; set; }
    }

    // ── Controller ────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/[controller]")]
    public class DocentesController : ControllerBase
    {
        private readonly IDocenteRepositorio _repo;

        public DocentesController(IDocenteRepositorio repo) => _repo = repo;

        [HttpGet]
        public async Task<ActionResult<List<DocenteDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.Select(MapToDto));
        }

        [HttpPost]
        public async Task<ActionResult<DocenteDto>> Create([FromBody] DocenteDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            try
            {
                var docente = new Docente(
                    id,
                    dto.Nombre,
                    "",
                    $"{id}@soea.local",
                    (decimal)dto.MaxHoras,
                    new List<FranjaHoraria> { FranjaHoraria.Matutino, FranjaHoraria.Vespertino }
                );
                docente.ActualizarPersistenciaUi(dto.Cedula, dto.Disponibilidad?.GetRawText());
                await _repo.AddAsync(docente);
                return StatusCode(StatusCodes.Status201Created, MapToDto(docente));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<DocenteDto>> Update(Guid id, [FromBody] DocenteDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound();
            try
            {
                existing.ActualizarDatos(dto.Nombre, "", null, (decimal)dto.MaxHoras);
                existing.ActualizarPersistenciaUi(dto.Cedula, dto.Disponibilidad?.GetRawText());
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
            await _repo.DeleteAsync(id);
            return NoContent();
        }

        private static DocenteDto MapToDto(Docente d)
        {
            JsonElement? disp = null;
            if (!string.IsNullOrEmpty(d.DisponibilidadUiJson))
            {
                try { disp = JsonSerializer.Deserialize<JsonElement>(d.DisponibilidadUiJson); }
                catch { /* ignore malformed stored JSON */ }
            }
            return new DocenteDto
            {
                Id = d.Id,
                Nombre = d.NombreCompleto.Trim(),
                Cedula = d.CedulaIdentidad ?? "",
                MaxHoras = (double)d.MaximoHorasSemanales,
                Disponibilidad = disp
            };
        }
    }
}
