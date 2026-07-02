using Microsoft.AspNetCore.Mvc;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.API.Controllers
{
    // ── DTOs ──────────────────────────────────────────────────────────────────────

    public class EspacioDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Tipo { get; set; } = "";
        public int Capacidad { get; set; }
        public string? Edificio { get; set; }
        public int? Piso { get; set; }
    }

    // ── Controller ────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/[controller]")]
    public class EspaciosController : ControllerBase
    {
        private readonly IEspacioRepositorio _repo;

        public EspaciosController(IEspacioRepositorio repo) => _repo = repo;

        [HttpGet]
        public async Task<ActionResult<List<EspacioDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.Select(MapToDto));
        }

        [HttpPost]
        public async Task<ActionResult<EspacioDto>> Create([FromBody] EspacioDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            try
            {
                var espacio = new Espacio(id, dto.Nombre, ParseTipo(dto.Tipo), dto.Capacidad, dto.Edificio, dto.Piso);
                await _repo.AddAsync(espacio);
                return StatusCode(StatusCodes.Status201Created, MapToDto(espacio));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<EspacioDto>> Update(Guid id, [FromBody] EspacioDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return NotFound();
            try
            {
                existing.ActualizarDatos(dto.Nombre, ParseTipo(dto.Tipo), dto.Edificio, dto.Piso);
                existing.ActualizarCapacidad(dto.Capacidad);
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
            if (existing is null) return NotFound($"Espacio con ID {id} no encontrado.");
            await _repo.DeleteAsync(id);
            return NoContent();
        }

        private static TipoEspacio ParseTipo(string tipo) => tipo switch
        {
            "Salón"       => TipoEspacio.Salon,
            "Laboratorio" => TipoEspacio.Laboratorio,
            "Auditorio"   => TipoEspacio.Auditorio,
            _             => throw new ArgumentException(
                $"Tipo de espacio '{tipo}' no reconocido. Valores válidos: 'Salón', 'Laboratorio', 'Auditorio'.")
        };

        private static EspacioDto MapToDto(Espacio e) => new()
        {
            Id = e.Id,
            Nombre = e.Nombre,
            Tipo = e.Tipo switch
            {
                TipoEspacio.Laboratorio => "Laboratorio",
                TipoEspacio.Auditorio   => "Auditorio",
                _                       => "Salón"
            },
            Capacidad = e.Capacidad,
            Edificio = e.Edificio,
            Piso = e.Piso
        };
    }
}
