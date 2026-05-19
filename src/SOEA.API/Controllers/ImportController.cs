using Microsoft.AspNetCore.Mvc;
using SOEA.Infrastructure.Data.Context;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using System.Globalization;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly SOEABdContext _context;

        public ImportController(SOEABdContext context)
        {
            _context = context;
        }

        [HttpGet("/api/facultades")]
        public IActionResult GetFacultades()
            => Ok(_context.Facultades
                .OrderBy(f => f.Nombre)
                .Select(f => new { id = f.Id.ToString(), nombre = f.Nombre })
                .ToList());

        [HttpGet("/api/programas")]
        public IActionResult GetProgramas()
            => Ok(_context.Programas
                .OrderBy(p => p.Nombre)
                .Select(p => new { id = p.Id.ToString(), nombre = p.Nombre, facultadId = p.FacultadId.ToString() })
                .ToList());

        [HttpPost("curriculum")]
        public async Task<IActionResult> ImportCurriculum([FromBody] CurriculumExcelDto dto)
        {
            if (dto == null) return BadRequest("Payload vacío");

            var createdFacultades = new List<MappingDto>();
            var createdProgramas = new List<MappingDto>();
            var createdAsignaturas = new List<MappingDto>();
            var createdGrupos = new List<MappingDto>();
            var createdDocentes = new List<MappingDto>();

            var facultadIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var programaIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var asignaturaIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var docenteIdMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            // Transacción para persistencia consistente
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Facultades
                foreach (var f in dto.Facultades ?? Enumerable.Empty<FacultadDto>())
                {
                    var exists = _context.Facultades.FirstOrDefault(x => x.Nombre.ToLower() == f.Nombre.ToLower());
                    Guid targetId;
                    if (exists == null)
                    {
                        var nueva = new Facultad(Guid.NewGuid(), f.Nombre);
                        _context.Facultades.Add(nueva);
                        targetId = nueva.Id;
                    }
                    else
                    {
                        targetId = exists.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(f.Id))
                    {
                        facultadIdMap[f.Id] = targetId;
                        createdFacultades.Add(new MappingDto { TempId = f.Id, NewId = targetId.ToString() });
                    }
                }
                await _context.SaveChangesAsync();

                // Programas
                foreach (var p in dto.Programas ?? Enumerable.Empty<ProgramaDto>())
                {
                    Guid facultadRealId;
                    if (!string.IsNullOrWhiteSpace(p.FacultadId) && facultadIdMap.TryGetValue(p.FacultadId, out var mappedFacId))
                    {
                        facultadRealId = mappedFacId;
                    }
                    else if (Guid.TryParse(p.FacultadId, out var facGuid))
                    {
                        facultadRealId = facGuid;
                    }
                    else
                    {
                        continue;
                    }

                    var fac = _context.Facultades.FirstOrDefault(x => x.Id == facultadRealId);
                    if (fac == null) continue;

                    var exists = _context.Programas.FirstOrDefault(x => x.Nombre.ToLower() == p.Nombre.ToLower() && x.FacultadId == fac.Id);
                    Guid targetId;
                    if (exists == null)
                    {
                        var nuevo = new Programa(Guid.NewGuid(), p.Nombre, fac.Id);
                        _context.Programas.Add(nuevo);
                        targetId = nuevo.Id;
                    }
                    else
                    {
                        targetId = exists.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(p.Id))
                    {
                        programaIdMap[p.Id] = targetId;
                        createdProgramas.Add(new MappingDto { TempId = p.Id, NewId = targetId.ToString() });
                    }
                }
                await _context.SaveChangesAsync();

                // Docentes
                foreach (var d in dto.Docentes ?? Enumerable.Empty<DocenteImportDto>())
                {
                    if (string.IsNullOrWhiteSpace(d.Nombre)) continue;
                    var exists = _context.Docentes.FirstOrDefault(x => x.Nombre.ToLower() == d.Nombre.ToLower());
                    Guid docenteTargetId;
                    if (exists == null)
                    {
                        var id = Guid.NewGuid();
                        var docente = new Docente(id, d.Nombre, "", $"{id}@soea.local",
                            (decimal)d.MaxHoras,
                            new List<Domain.Enums.FranjaHoraria> { Domain.Enums.FranjaHoraria.Matutino, Domain.Enums.FranjaHoraria.Vespertino });
                        if (!string.IsNullOrWhiteSpace(d.Cedula))
                            docente.ActualizarPersistenciaUi(d.Cedula, null);
                        _context.Docentes.Add(docente);
                        docenteTargetId = id;
                    }
                    else
                    {
                        docenteTargetId = exists.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(d.Id))
                    {
                        docenteIdMap[d.Id] = docenteTargetId;
                        createdDocentes.Add(new MappingDto { TempId = d.Id, NewId = docenteTargetId.ToString() });
                    }
                }
                await _context.SaveChangesAsync();

                // Espacios
                foreach (var e in dto.Espacios ?? Enumerable.Empty<EspacioImportDto>())
                {
                    if (string.IsNullOrWhiteSpace(e.Nombre)) continue;
                    var exists = _context.Espacios.FirstOrDefault(x => x.Nombre.ToLower() == e.Nombre.ToLower());
                    if (exists == null)
                    {
                        var tipo = e.Tipo switch
                        {
                            "Laboratorio" => Domain.Enums.TipoEspacio.Laboratorio,
                            "Auditorio"   => Domain.Enums.TipoEspacio.Auditorio,
                            _             => Domain.Enums.TipoEspacio.Salon
                        };
                        _context.Espacios.Add(new Espacio(Guid.NewGuid(), e.Nombre, tipo, e.Capacidad, e.Edificio, e.Piso));
                    }
                }
                await _context.SaveChangesAsync();

                // Asignaturas (DTO -> entidad)
                foreach (var a in dto.Asignaturas ?? Enumerable.Empty<AsignaturaDto>())
                {
                    Guid programaRealId;
                    if (!string.IsNullOrWhiteSpace(a.ProgramaId) && programaIdMap.TryGetValue(a.ProgramaId, out var mappedProgId))
                    {
                        programaRealId = mappedProgId;
                    }
                    else if (Guid.TryParse(a.ProgramaId, out var progGuid))
                    {
                        programaRealId = progGuid;
                    }
                    else
                    {
                        continue;
                    }

                    var prog = _context.Programas.FirstOrDefault(x => x.Id == programaRealId);
                    if (prog == null) continue;

                    Guid? docenteRealId = null;
                    if (!string.IsNullOrWhiteSpace(a.DocenteId))
                    {
                        if (docenteIdMap.TryGetValue(a.DocenteId, out var mappedDocId))
                            docenteRealId = mappedDocId;
                        else if (Guid.TryParse(a.DocenteId, out var docGuid))
                            docenteRealId = docGuid;
                    }

                    var exists = _context.Asignaturas.FirstOrDefault(x => x.Nombre.ToLower() == a.Nombre.ToLower() && x.ProgramaId == prog.Id);
                    Guid targetId;
                    if (exists == null)
                    {
                        var entidad = new Asignatura(Guid.NewGuid(), a.Nombre, a.Codigo ?? string.Empty, a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, prog.Id);
                        entidad.AsignarDocente(docenteRealId);
                        _context.Asignaturas.Add(entidad);
                        targetId = entidad.Id;
                    }
                    else
                    {
                        exists.AsignarDocente(docenteRealId);
                        targetId = exists.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(a.Id))
                    {
                        asignaturaIdMap[a.Id] = targetId;
                        createdAsignaturas.Add(new MappingDto { TempId = a.Id, NewId = targetId.ToString() });
                    }
                }
                await _context.SaveChangesAsync();

                // Grupos: crear por cada asignatura importada con grupoNumero (si no viene, usar 1)
                foreach (var a in dto.Asignaturas ?? Enumerable.Empty<AsignaturaDto>())
                {
                    Guid programaRealId;
                    if (!string.IsNullOrWhiteSpace(a.ProgramaId) && programaIdMap.TryGetValue(a.ProgramaId, out var mappedProgId2))
                    {
                        programaRealId = mappedProgId2;
                    }
                    else if (Guid.TryParse(a.ProgramaId, out var progGuid2))
                    {
                        programaRealId = progGuid2;
                    }
                    else
                    {
                        continue;
                    }

                    var asign = _context.Asignaturas.FirstOrDefault(x => x.Nombre.ToLower() == a.Nombre.ToLower() && x.ProgramaId == programaRealId);
                    if (asign == null) continue;

                    var numeroGrupo = a.GrupoNumero.GetValueOrDefault(1);
                    var groupName = $"{a.Nombre} - Grupo {numeroGrupo}";

                    var existente = _context.Grupos.FirstOrDefault(g =>
                        g.Nombre.ToLower() == groupName.ToLower() &&
                        g.ProgramaId == asign.ProgramaId);

                    Guid grupoId;
                    if (existente == null)
                    {
                        var grupo = new Grupo(Guid.NewGuid(), groupName, asign.ProgramaId, 1, 30, asign.Alternancia);
                        _context.Grupos.Add(grupo);
                        grupoId = grupo.Id;
                    }
                    else
                    {
                        grupoId = existente.Id;
                    }

                    var tempGroupKey = BuildGroupTempKey(a.Id, numeroGrupo);
                    createdGrupos.Add(new MappingDto { TempId = tempGroupKey, NewId = grupoId.ToString() });
                }
                await _context.SaveChangesAsync();

                // Sesiones predefinidas (opcionales — solo si vienen con BloqueTiempoId válido)
                foreach (var s in dto.SesionesPredefinidas ?? Enumerable.Empty<SesionImportDto>())
                {
                    if (s.BloqueTiempoId == Guid.Empty) continue;
                    var asign = _context.Asignaturas.FirstOrDefault(x => x.Id == s.AsignaturaId);
                    if (asign == null) continue;
                    var alt = Enum.TryParse<TipoAlternancia>(s.Alternancia, out var parsed)
                        ? parsed : TipoAlternancia.SinAlternancia;
                    var sesion = new Sesion(
                        Guid.NewGuid(), s.AsignaturaId, s.DocenteId,
                        s.BloqueTiempoId, s.EspacioId, s.GrupoId,
                        alt, Modalidad.Presencial, s.DuracionHoras,
                        esBloque: false, estaDividida: false);
                    _context.Sesiones.Add(sesion);
                }
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                var result = new ImportResultDto
                {
                    Facultades = createdFacultades,
                    Programas = createdProgramas,
                    Asignaturas = createdAsignaturas,
                    Grupos = createdGrupos,
                    Docentes = createdDocentes,
                    Summary = new ImportSummaryDto
                    {
                        Facultades = createdFacultades.Count,
                        Programas = createdProgramas.Count,
                        Asignaturas = createdAsignaturas.Count,
                        Grupos = createdGrupos.Count,
                        Docentes = createdDocentes.Count
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        private static string BuildGroupTempKey(string? asignaturaTempId, int grupoNumero)
        {
            var left = string.IsNullOrWhiteSpace(asignaturaTempId) ? "no-id" : asignaturaTempId;
            return string.Create(CultureInfo.InvariantCulture, $"{left}:{grupoNumero}");
        }
    }

    // DTOs simplificados para el endpoint de importación
    public record FacultadDto(string Id, string Nombre);
    public record ProgramaDto(string Id, string Nombre, string FacultadId);

    public class CurriculumExcelDto
    {
        public List<FacultadDto>? Facultades { get; set; }
        public List<ProgramaDto>? Programas { get; set; }
        public List<AsignaturaDto>? Asignaturas { get; set; }
        public List<DocenteImportDto>? Docentes { get; set; }
        public List<SesionImportDto>? SesionesPredefinidas { get; set; }
        public List<EspacioImportDto>? Espacios { get; set; }
    }

    public class DocenteImportDto
    {
        public string? Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Cedula { get; set; } = string.Empty;
        public double MaxHoras { get; set; } = 40;
        public object? Disponibilidad { get; set; }
    }

    public class EspacioImportDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Tipo { get; set; } = "Salón";
        public int Capacidad { get; set; } = 30;
        public string? Edificio { get; set; }
        public int? Piso { get; set; }
    }

    public class SesionImportDto
    {
        public Guid AsignaturaId { get; set; }
        public Guid DocenteId { get; set; }
        public Guid BloqueTiempoId { get; set; }
        public Guid? EspacioId { get; set; }
        public Guid? GrupoId { get; set; }
        public string Alternancia { get; set; } = "SinAlternancia";
        public decimal DuracionHoras { get; set; } = 2;
    }

    public class AsignaturaDto
    {
        public string? Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Codigo { get; set; }
        public string? DocenteId { get; set; }
        public int HorasPorSesion { get; set; }
        public int SesionesPorSemana { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public string ProgramaId { get; set; } = string.Empty;
        public string Alternancia { get; set; } = "SinAlternancia";
        public int? GrupoNumero { get; set; }
    }

    public class MappingDto
    {
        public string TempId { get; set; } = string.Empty;
        public string NewId { get; set; } = string.Empty;
    }

    public class ImportSummaryDto
    {
        public int Facultades { get; set; }
        public int Programas { get; set; }
        public int Asignaturas { get; set; }
        public int Grupos { get; set; }
        public int Docentes { get; set; }
    }

    public class ImportResultDto
    {
        public List<MappingDto> Facultades { get; set; } = new();
        public List<MappingDto> Programas { get; set; } = new();
        public List<MappingDto> Asignaturas { get; set; } = new();
        public List<MappingDto> Grupos { get; set; } = new();
        public List<MappingDto> Docentes { get; set; } = new();
        public ImportSummaryDto Summary { get; set; } = new();
    }
}
