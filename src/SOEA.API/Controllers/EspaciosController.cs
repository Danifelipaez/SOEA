using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Espacios;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

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
        private readonly EspacioService _service;

        public EspaciosController(EspacioService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<EspacioDto>>> GetAll() =>
            Ok((await _service.ListarAsync()).Select(MapToDto));

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce ArgumentException a 400,
        // KeyNotFoundException a 404 y BusinessRuleViolationException a 409.
        [HttpPost]
        public async Task<ActionResult<EspacioDto>> Create([FromBody] EspacioDto dto) =>
            StatusCode(StatusCodes.Status201Created, MapToDto(
                await _service.CrearAsync(dto.Id, dto.Nombre, ParseTipo(dto.Tipo), dto.Capacidad, dto.Edificio, dto.Piso)));

        [HttpPut("{id}")]
        public async Task<ActionResult<EspacioDto>> Update(Guid id, [FromBody] EspacioDto dto) =>
            Ok(MapToDto(await _service.ActualizarAsync(id, dto.Nombre, ParseTipo(dto.Tipo), dto.Capacidad, dto.Edificio, dto.Piso)));

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        // L-12 auditoría 2026-09-28: antes exigía "Salón" con tilde y exacto — "Salon" (el literal que usa el
        // resto del contrato, p. ej. RequisitoEspacioDto) daba 400. Se comparan sin tilde ni mayúsculas.
        private static TipoEspacio ParseTipo(string tipo) => NormalizadorTexto.Normalizar(tipo) switch
        {
            "salon"       => TipoEspacio.Salon,
            "laboratorio" => TipoEspacio.Laboratorio,
            "auditorio"   => TipoEspacio.Auditorio,
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
