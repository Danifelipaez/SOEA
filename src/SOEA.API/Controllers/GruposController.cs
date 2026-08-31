using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public GruposController(IGrupoRepositorio repo, IAsignaturaRepositorio asignaturas)
        {
            _repo = repo;
            _asignaturas = asignaturas;
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
            try
            {
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

                await _repo.AddAsync(grupo);
                return StatusCode(StatusCodes.Status201Created, MapToDto(grupo));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (DbUpdateException)
            {
                // G6 auditoría: el índice único ix_grupo_codigo (único constraint del Grupo)
                // lanzaba DbUpdateException sin capturar → 500 genérico. El frontend lo pintaba
                // como "no se guarda, no crea grupo" sin decir por qué.
                return Conflict($"Ya existe un grupo con el código '{dto.Codigo}'. Use un código distinto.");
            }
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

            try
            {
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
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (DbUpdateException)
            {
                return Conflict($"Ya existe un grupo con el código '{dto.Codigo}'. Use un código distinto.");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var grupo = await _repo.GetByIdAsync(id);
            if (grupo is null) return NotFound($"Grupo con Id {id} no encontrado.");
            await _repo.DeleteAsync(id);
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
