using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Grupos;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;

namespace SOEA.API.Controllers
{
    // ── DTOs ──────────────────────────────────────────────────────────────────────

    public class GrupoDto
    {
        public Guid Id { get; set; }
        /// <summary>Asignatura a la que pertenece el grupo. Obligatorio en creación y edición.</summary>
        public Guid? AsignaturaId { get; set; }
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
        private readonly IGrupoRepositorio _repo;
        private readonly IAsignaturaRepositorio _asignaturas;
        private readonly GrupoService _service;

        public GruposController(IGrupoRepositorio repo, IAsignaturaRepositorio asignaturas, GrupoService service)
        {
            _repo = repo;
            _asignaturas = asignaturas;
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<List<GrupoDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return Ok(list.Select(MapToDto));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GrupoDto>> GetById(Guid id)
        {
            var g = await _repo.GetByIdAsync(id);
            return g is null ? NotFound() : Ok(MapToDto(g));
        }

        [HttpGet("por-asignatura/{asignaturaId}")]
        public async Task<ActionResult<List<GrupoDto>>> GetByAsignatura(Guid asignaturaId)
        {
            var list = await _repo.GetByAsignaturaIdAsync(asignaturaId);
            return Ok(list.Select(MapToDto));
        }

        [HttpPost]
        public async Task<ActionResult<GrupoDto>> Create([FromBody] GrupoDto dto)
        {
            // Invariante: todo grupo debe estar atado a una asignatura en creación.
            if (dto.AsignaturaId is null || dto.AsignaturaId == Guid.Empty)
                return BadRequest("AsignaturaId es obligatorio al crear un grupo.");
            // G6 auditoría: sin esto, un ProgramaId vacío se persistía sin error — el grupo
            // quedaba luego con "Guardar" deshabilitado al editarlo (el select de programa, con
            // Validators.required, nunca aceptaba un valor vacío para volver a habilitarlo).
            if (dto.ProgramaId == Guid.Empty)
                return BadRequest("ProgramaId es obligatorio al crear un grupo.");

            var asignatura = await _asignaturas.GetByIdAsync(dto.AsignaturaId.Value);
            if (asignatura is null)
                return BadRequest($"No existe la asignatura con Id '{dto.AsignaturaId}'.");

            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            // ERR2 auditoría: sin catch de ArgumentException — GlobalExceptionHandler lo traduce a 400.
            var grupo = new Grupo(
                id,
                dto.Nombre,
                dto.ProgramaId,
                dto.EstudiantesInscritos,
                asignaturaId: dto.AsignaturaId,
                facultadId: dto.FacultadId,
                docenteId: dto.DocenteId,
                codigo: dto.Codigo);

            grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
            grupo.ActualizarRequisitosEspacio(MapearRequisitosEspacio(dto.RequisitosEspacio));

            // G6 auditoría: el índice único ix_grupo_codigo (único constraint del Grupo) lanzaba
            // DbUpdateException sin capturar → 500 genérico. Bug (auditoría de limpieza, hallazgo
            // 1.8): el catch que arreglaba eso aquí asumía que CUALQUIER DbUpdateException era el
            // código duplicado — una violación de FK, de NOT NULL o cualquier otra restricción
            // salía con el mismo mensaje falso. GlobalExceptionHandler ya traduce DbUpdateException
            // a 409 con un mensaje genérico correcto ("ya existe un registro con esos datos, o hace
            // referencia a algo que no existe"); se deja que llegue ahí en vez de afirmar una causa
            // que este catch no puede conocer.
            await _repo.AddAsync(grupo);
            return StatusCode(StatusCodes.Status201Created, MapToDto(grupo));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<GrupoDto>> Update(Guid id, [FromBody] GrupoDto dto)
        {
            // Invariante: todo grupo debe estar atado a una asignatura.
            if (dto.AsignaturaId is null || dto.AsignaturaId == Guid.Empty)
                return BadRequest("AsignaturaId es obligatorio.");

            var grupo = await _repo.GetByIdAsync(id);
            if (grupo is null) return NotFound();

            var asignatura = await _asignaturas.GetByIdAsync(dto.AsignaturaId.Value);
            if (asignatura is null)
                return BadRequest($"No existe la asignatura con Id '{dto.AsignaturaId}'.");

            // ERR2 auditoría: sin catch de ArgumentException — GlobalExceptionHandler lo traduce a
            // 400. Ver comentario en Create: GlobalExceptionHandler también traduce DbUpdateException.
            grupo.ActualizarNombre(dto.Nombre);
            grupo.ActualizarCodigo(dto.Codigo);
            grupo.ActualizarPrograma(dto.ProgramaId);
            grupo.ActualizarEstudiantes(dto.EstudiantesInscritos);
            grupo.ActualizarAsignatura(dto.AsignaturaId, dto.FacultadId ?? grupo.FacultadId);
            grupo.AsignarDocente(dto.DocenteId);
            grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
            grupo.ActualizarRequisitosEspacio(MapearRequisitosEspacio(dto.RequisitosEspacio));

            await _repo.UpdateAsync(grupo);
            return Ok(MapToDto(grupo));
        }

        // ERR2 auditoría: sin catch — GlobalExceptionHandler traduce KeyNotFoundException a 404.
        // Antes este endpoint bloqueaba el borrado con 409 si el grupo tenía sesiones generadas
        // (M14 auditoría) — el catálogo no debe bloquearse por datos de una corrida, que son
        // regenerables. GrupoService.DeleteAsync purga esas sesiones en cascada.
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }

        private static GrupoDto MapToDto(Grupo g) => new()
        {
            Id = g.Id,
            AsignaturaId = g.AsignaturaId,
            ProgramaId = g.ProgramaId,
            FacultadId = g.FacultadId,
            DocenteId = g.DocenteId,
            Nombre = g.Nombre,
            Codigo = g.Codigo,
            EstudiantesInscritos = g.EstudiantesInscritos,
            DisponibilidadUiJson = g.DisponibilidadUiJson,
            RequisitosEspacio = g.RequisitosEspacio
                .Select(r => new RequisitoEspacioDto
                {
                    TipoSesion = r.TipoSesion,
                    EspacioId = r.EspacioId,
                    TipoEspacio = r.TipoEspacio,
                    Sesiones = r.Sesiones
                })
                .ToList()
        };

        private static List<RequisitoEspacio> MapearRequisitosEspacio(List<RequisitoEspacioDto> dtos) =>
            dtos.Select(d => new RequisitoEspacio(d.TipoSesion, d.EspacioId, d.TipoEspacio, d.Sesiones)).ToList();
    }
}
