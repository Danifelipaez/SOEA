using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Grupos;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.API.Controllers
{
    // ── DTOs ──────────────────────────────────────────────────────────────────────

    public class GrupoDto
    {
        public Guid Id { get; set; }
        /// <summary>Asignatura a la que pertenece el grupo. Obligatorio en creación y edición.</summary>
        public Guid? AsignaturaId { get; set; }
        /// <summary>Solo lectura: derivados de la asignatura (Grupo → Asignatura → Programa → Facultad).
        /// Se ignoran al crear o editar.</summary>
        public Guid ProgramaId { get; set; }
        public Guid? FacultadId { get; set; }
        /// <summary>Docente que dicta la asignatura para este grupo (opcional).</summary>
        public Guid? DocenteId { get; set; }
        public string Nombre { get; set; } = "";
        public string? Codigo { get; set; }
        public int EstudiantesInscritos { get; set; }
        /// <summary>JSON de disponibilidad tal como viene de la UI (por día: {lunes:{...}, ...}).</summary>
        public string? DisponibilidadUiJson { get; set; }
        /// <summary>Requisito de espacio por tipo de sesión (HC-S03/HC-S05).</summary>
        public List<RequisitoEspacioDto> RequisitosEspacio { get; set; } = new();
    }

    public class RequisitoEspacioDto
    {
        /// <summary>TeoriaPresencial | TeoriaVirtual | Laboratorio.</summary>
        public TipoSesion TipoSesion { get; set; }
        /// <summary>Espacio concreto exigido. Null = cualquier espacio de <see cref="TipoEspacio"/>.</summary>
        public Guid? EspacioId { get; set; }
        /// <summary>Null = sin preferencia de tipo (M6): usa la regla por defecto según TipoSesion.</summary>
        public TipoEspacio? TipoEspacio { get; set; }
        public int Sesiones { get; set; }
    }

    // ── Controller ────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/[controller]")]
    public class GruposController : ControllerBase
    {
        private readonly GrupoService _service;

        public GruposController(GrupoService service) => _service = service;

        [HttpGet]
        public async Task<ActionResult<List<GrupoDto>>> GetAll() =>
            Ok((await _service.ListarAsync()).Select(MapToDto));

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce KeyNotFoundException a 404.
        [HttpGet("{id}")]
        public async Task<ActionResult<GrupoDto>> GetById(Guid id) =>
            Ok(MapToDto(await _service.ObtenerAsync(id)));

        [HttpGet("por-asignatura/{asignaturaId}")]
        public async Task<ActionResult<List<GrupoDto>>> GetByAsignatura(Guid asignaturaId) =>
            Ok((await _service.ListarPorAsignaturaAsync(asignaturaId)).Select(MapToDto));

        [HttpPost]
        public async Task<ActionResult<GrupoDto>> Create([FromBody] GrupoDto dto) =>
            StatusCode(StatusCodes.Status201Created, MapToDto(await _service.CrearAsync(dto.Id, Datos(dto))));

        [HttpPut("{id}")]
        public async Task<ActionResult<GrupoDto>> Update(Guid id, [FromBody] GrupoDto dto) =>
            Ok(MapToDto(await _service.ActualizarAsync(id, Datos(dto))));

        // Antes este endpoint bloqueaba el borrado con 409 si el grupo tenía sesiones generadas
        // (M14 auditoría) — el catálogo no debe bloquearse por datos de una corrida, que son
        // regenerables. GrupoService.DeleteAsync purga esas sesiones en cascada.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static GrupoDatos Datos(GrupoDto dto) => new(
            dto.AsignaturaId, dto.Nombre, dto.Codigo, dto.EstudiantesInscritos, dto.DocenteId, dto.DisponibilidadUiJson,
            dto.RequisitosEspacio.Select(d => new RequisitoEspacio(d.TipoSesion, d.EspacioId, d.TipoEspacio, d.Sesiones)).ToList());

        private static GrupoDto MapToDto(GrupoConJerarquia x) => new()
        {
            Id = x.Grupo.Id,
            AsignaturaId = x.Grupo.AsignaturaId,
            ProgramaId = x.ProgramaId,
            FacultadId = x.FacultadId,
            DocenteId = x.Grupo.DocenteId,
            Nombre = x.Grupo.Nombre,
            Codigo = x.Grupo.Codigo,
            EstudiantesInscritos = x.Grupo.EstudiantesInscritos,
            DisponibilidadUiJson = x.Grupo.DisponibilidadUiJson,
            RequisitosEspacio = x.Grupo.RequisitosEspacio
                .Select(r => new RequisitoEspacioDto
                {
                    TipoSesion = r.TipoSesion,
                    EspacioId = r.EspacioId,
                    TipoEspacio = r.TipoEspacio,
                    Sesiones = r.Sesiones
                })
                .ToList()
        };
    }
}
