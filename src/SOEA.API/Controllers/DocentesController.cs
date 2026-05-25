using Microsoft.AspNetCore.Mvc;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using System.Text.Json;
using System.Text.Json.Nodes;

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
                existing.ActualizarDatos(dto.Nombre, "", existing.Correo, (decimal)dto.MaxHoras);
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

        private static readonly Dictionary<DiaDeSemana, string> _diaKey = new()
        {
            { DiaDeSemana.Lunes,     "lunes"     },
            { DiaDeSemana.Martes,    "martes"    },
            { DiaDeSemana.Miercoles, "miercoles" },
            { DiaDeSemana.Jueves,    "jueves"    },
            { DiaDeSemana.Viernes,   "viernes"   },
            { DiaDeSemana.Sábado,    "sabado"    },
        };

        private static DocenteDto MapToDto(Docente d)
        {
            JsonElement? disp = null;

            // Prefer the hand-edited UI JSON; fall back to deriving it from imported blocks.
            if (!string.IsNullOrEmpty(d.DisponibilidadUiJson))
            {
                try { disp = JsonSerializer.Deserialize<JsonElement>(d.DisponibilidadUiJson); }
                catch { /* ignore malformed */ }
            }
            else if (d.BloquesDisponibles.Count > 0)
            {
                // Build per-day disponibilidad from the 1-hour catalog blocks imported from Excel.
                // For each day: find earliest HoraInicio and latest HoraFin among all blocks.
                var porDia = d.BloquesDisponibles
                    .GroupBy(b => b.Dia)
                    .ToDictionary(g => g.Key, g => (
                        desde: g.Min(b => b.HoraInicio),
                        hasta: g.Max(b => b.HoraFin)
                    ));

                var obj = new JsonObject();
                foreach (var (dia, key) in _diaKey)
                {
                    if (porDia.TryGetValue(dia, out var rango))
                    {
                        obj[key] = new JsonObject
                        {
                            ["noDisponible"] = false,
                            ["tipo"]         = "especifico",
                            ["desde"]        = rango.desde.ToString("HH:mm"),
                            ["hasta"]        = rango.hasta.ToString("HH:mm")
                        };
                    }
                    else
                    {
                        obj[key] = new JsonObject { ["noDisponible"] = true };
                    }
                }
                disp = JsonSerializer.Deserialize<JsonElement>(obj.ToJsonString());
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
