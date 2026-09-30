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
        private readonly IGrupoRepositorio _repo;
        private readonly IAsignaturaRepositorio _asignaturas;
        private readonly IProgramaRepositorio _programas;
        private readonly GrupoService _service;

        public GruposController(IGrupoRepositorio repo, IAsignaturaRepositorio asignaturas,
            IProgramaRepositorio programas, GrupoService service)
        {
            _repo = repo;
            _asignaturas = asignaturas;
            _programas = programas;
            _service = service;
        }

        /// <summary>asignaturaId → (programaId, facultadId), para rellenar los campos derivados del DTO.</summary>
        private async Task<Dictionary<Guid, (Guid ProgramaId, Guid? FacultadId)>> JerarquiaAsync()
        {
            var facultadPorPrograma = (await _programas.GetAllAsync()).ToDictionary(p => p.Id, p => p.FacultadId);
            return (await _asignaturas.GetAllAsync()).ToDictionary(a => a.Id,
                a => (a.ProgramaId, facultadPorPrograma.TryGetValue(a.ProgramaId, out var f) ? f : (Guid?)null));
        }

        /// <summary>Igual que <see cref="JerarquiaAsync"/> para un solo grupo: sin cargar tablas completas.</summary>
        private async Task<Dictionary<Guid, (Guid ProgramaId, Guid? FacultadId)>> JerarquiaDeAsync(Asignatura? a) =>
            a is null ? new() : new() { [a.Id] = (a.ProgramaId, (await _programas.GetByIdAsync(a.ProgramaId))?.FacultadId) };

        [HttpGet]
        public async Task<ActionResult<List<GrupoDto>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            var jerarquia = await JerarquiaAsync();
            return Ok(list.Select(g => MapToDto(g, jerarquia)));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GrupoDto>> GetById(Guid id)
        {
            var g = await _repo.GetByIdAsync(id);
            if (g is null) throw new KeyNotFoundException($"Grupo con ID {id} no encontrado.");
            return Ok(MapToDto(g, await JerarquiaDeAsync(await _asignaturas.GetByIdAsync(g.AsignaturaId))));
        }

        [HttpGet("por-asignatura/{asignaturaId}")]
        public async Task<ActionResult<List<GrupoDto>>> GetByAsignatura(Guid asignaturaId)
        {
            var list = await _repo.GetByAsignaturaIdAsync(asignaturaId);
            var jerarquia = await JerarquiaAsync();
            return Ok(list.Select(g => MapToDto(g, jerarquia)));
        }

        [HttpPost]
        public async Task<ActionResult<GrupoDto>> Create([FromBody] GrupoDto dto)
        {
            // Invariante: todo grupo debe estar atado a una asignatura en creación.
            if (dto.AsignaturaId is null || dto.AsignaturaId == Guid.Empty)
                throw new ArgumentException("AsignaturaId es obligatorio al crear un grupo.");

            var asignatura = await _asignaturas.GetByIdAsync(dto.AsignaturaId.Value);
            if (asignatura is null)
                throw new ArgumentException($"No existe la asignatura con Id '{dto.AsignaturaId}'.");

            var requisitos = MapearRequisitosEspacio(dto.RequisitosEspacio);
            GrupoService.ValidarDatos(dto.DisponibilidadUiJson, requisitos);

            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            // ERR2 auditoría: sin catch de ArgumentException — GlobalExceptionHandler lo traduce a 400.
            var grupo = new Grupo(
                id,
                dto.Nombre,
                asignatura.Id,
                dto.EstudiantesInscritos,
                docenteId: dto.DocenteId,
                codigo: dto.Codigo);

            grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
            grupo.ActualizarRequisitosEspacio(requisitos);

            // G6 auditoría: el índice único ix_grupo_codigo (único constraint del Grupo) lanzaba
            // DbUpdateException sin capturar → 500 genérico. Bug (auditoría de limpieza, hallazgo
            // 1.8): el catch que arreglaba eso aquí asumía que CUALQUIER DbUpdateException era el
            // código duplicado — una violación de FK, de NOT NULL o cualquier otra restricción
            // salía con el mismo mensaje falso. GlobalExceptionHandler ya traduce DbUpdateException
            // a 409 con un mensaje genérico correcto ("ya existe un registro con esos datos, o hace
            // referencia a algo que no existe"); se deja que llegue ahí en vez de afirmar una causa
            // que este catch no puede conocer.
            await _repo.AddAsync(grupo);
            return StatusCode(StatusCodes.Status201Created, MapToDto(grupo, await JerarquiaDeAsync(asignatura)));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<GrupoDto>> Update(Guid id, [FromBody] GrupoDto dto)
        {
            // Invariante: todo grupo debe estar atado a una asignatura.
            if (dto.AsignaturaId is null || dto.AsignaturaId == Guid.Empty)
                throw new ArgumentException("AsignaturaId es obligatorio.");

            var grupo = await _repo.GetByIdAsync(id);
            if (grupo is null) throw new KeyNotFoundException($"Grupo con ID {id} no encontrado.");

            var asignatura = await _asignaturas.GetByIdAsync(dto.AsignaturaId.Value);
            if (asignatura is null)
                throw new ArgumentException($"No existe la asignatura con Id '{dto.AsignaturaId}'.");

            var requisitos = MapearRequisitosEspacio(dto.RequisitosEspacio);
            GrupoService.ValidarDatos(dto.DisponibilidadUiJson, requisitos);

            // ERR2 auditoría: sin catch de ArgumentException — GlobalExceptionHandler lo traduce a
            // 400. Ver comentario en Create: GlobalExceptionHandler también traduce DbUpdateException.
            grupo.ActualizarNombre(dto.Nombre);
            grupo.ActualizarCodigo(dto.Codigo);
            grupo.ActualizarEstudiantes(dto.EstudiantesInscritos);
            // Si cambia la asignatura, la FK compuesta de Sesiones (ON UPDATE CASCADE) mueve con él
            // las sesiones del grupo: nunca quedan de una asignatura distinta a la de su grupo.
            grupo.ActualizarAsignatura(asignatura.Id);
            grupo.AsignarDocente(dto.DocenteId);
            grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
            grupo.ActualizarRequisitosEspacio(requisitos);

            await _repo.UpdateAsync(grupo);
            return Ok(MapToDto(grupo, await JerarquiaDeAsync(asignatura)));
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

        private static GrupoDto MapToDto(Grupo g, Dictionary<Guid, (Guid ProgramaId, Guid? FacultadId)> jerarquia) => new()
        {
            Id = g.Id,
            AsignaturaId = g.AsignaturaId,
            ProgramaId = jerarquia.TryGetValue(g.AsignaturaId, out var j) ? j.ProgramaId : Guid.Empty,
            FacultadId = jerarquia.TryGetValue(g.AsignaturaId, out var j2) ? j2.FacultadId : null,
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
