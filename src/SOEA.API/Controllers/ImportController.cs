using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SOEA.Application.Features.Import;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly ImportarCurriculumService _importService;
        private readonly ILectorExcel _lectorExcel;
        private readonly IBloqueTiempoRepositorio _bloques;
        private readonly IFacultadRepositorio _facultades;
        private readonly IProgramaRepositorio _programas;

        public ImportController(
            ImportarCurriculumService importService,
            ILectorExcel lectorExcel,
            IBloqueTiempoRepositorio bloques,
            IFacultadRepositorio facultades,
            IProgramaRepositorio programas)
        {
            _importService = importService;
            _lectorExcel   = lectorExcel;
            _bloques       = bloques;
            _facultades    = facultades;
            _programas     = programas;
        }

        /// <summary>
        /// Importa el Excel de horario/currículum y persiste toda la jerarquía en una transacción.
        /// </summary>
        [HttpPost("excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportarExcel(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return BadRequest("No se recibió ningún archivo.");

            if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !archivo.FileName.EndsWith(".xls",  StringComparison.OrdinalIgnoreCase))
                return BadRequest("El archivo debe ser .xlsx o .xls.");

            var bloquesCatalogo = await _bloques.GetAllAsync();
            var catalogoLookup = bloquesCatalogo
                .GroupBy(b => (b.Dia, b.HoraInicio))
                .ToDictionary(g => g.Key, g => g.First())
                as IReadOnlyDictionary<(DiaDeSemana, TimeOnly), BloqueTiempo>;

            CurriculumExcelResult resultado;
            try
            {
                using var stream = archivo.OpenReadStream();
                resultado = await _lectorExcel.LeerCurriculumAsync(stream, catalogoLookup);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al leer el Excel: {ex.Message}");
            }

            ImportarCurriculumStats stats;
            try
            {
                stats = await _importService.EjecutarAsync(resultado);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al persistir: {ex.Message}");
            }

            return Ok(new ImportExcelStatsDto
            {
                FacultadesCreadas       = stats.FacultadesCreadas,
                ProgramasCreados        = stats.ProgramasCreados,
                DocentesCreados         = stats.DocentesCreados,
                DocentesActualizados    = stats.DocentesActualizados,
                EspaciosCreados         = stats.EspaciosCreados,
                AsignaturasCreadas      = stats.AsignaturasCreadas,
                AsignaturasActualizadas = stats.AsignaturasActualizadas,
                GruposCreados           = stats.GruposCreados,
                SesionesPersistidas     = stats.SesionesPersistidas,
                AsignaturasSinDocente   = stats.AsignaturasSinDocente,
                Advertencias            = stats.Advertencias
            });
        }

        [HttpGet("/api/facultades")]
        public async Task<IActionResult> GetFacultades()
            => Ok((await _facultades.GetAllAsync())
                .OrderBy(f => f.Nombre)
                .Select(f => new { id = f.Id.ToString(), nombre = f.Nombre }));

        [HttpGet("/api/programas")]
        public async Task<IActionResult> GetProgramas()
            => Ok((await _programas.GetAllAsync())
                .OrderBy(p => p.Nombre)
                .Select(p => new { id = p.Id.ToString(), nombre = p.Nombre, facultadId = p.FacultadId.ToString() }));

        /// <summary>
        /// Recibe la jerarquía curricular en JSON con IDs temporales del cliente
        /// y devuelve los mapas tempId → realId para que el frontend actualice su estado.
        /// </summary>
        [HttpPost("curriculum")]
        public async Task<IActionResult> ImportCurriculum([FromBody] CurriculumExcelDto dto)
        {
            if (dto == null) return BadRequest("Payload vacío");

            var facStrGuid   = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var progStrGuid  = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var docStrGuid   = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var asigStrGuid  = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var grupoStrGuid = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            // ── Mapeo DTO → entidades de dominio con IDs temporales ────────────────
            var facultades  = MapFacultadesDto(dto.Facultades, facStrGuid);
            var programas   = MapProgramasDto(dto.Programas, facStrGuid, progStrGuid);
            var docentes    = MapDocentesDto(dto.Docentes, docStrGuid);
            var espacios    = MapEspaciosDto(dto.Espacios);
            var asignaturas = MapAsignaturasDto(dto.Asignaturas, progStrGuid, docStrGuid, asigStrGuid);
            var grupos      = MapGruposDeAsignaturas(dto.Asignaturas, progStrGuid, asigStrGuid, grupoStrGuid);
            var sesiones    = MapSesionesDto(dto.SesionesPredefinidas);

            var resultado = new CurriculumExcelResult(
                facultades, programas, asignaturas, docentes,
                sesiones, espacios, grupos);

            ImportarCurriculumStats stats;
            try
            {
                stats = await _importService.EjecutarAsync(resultado);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }

            return Ok(new ImportResultDto
            {
                Facultades  = MapearIdList(facStrGuid,   stats.FacultadMappings),
                Programas   = MapearIdList(progStrGuid,  stats.ProgramaMappings),
                Asignaturas = MapearIdList(asigStrGuid,  stats.AsignaturaMappings),
                Docentes    = MapearIdList(docStrGuid,   stats.DocenteMappings),
                Grupos      = MapearIdList(grupoStrGuid, stats.GrupoMappings),
                Summary = new ImportSummaryDto
                {
                    Facultades  = facStrGuid.Count,
                    Programas   = progStrGuid.Count,
                    Asignaturas = asigStrGuid.Count,
                    Grupos      = grupoStrGuid.Count,
                    Docentes    = docStrGuid.Count
                }
            });
        }

        // ── Helpers de mapeo DTO → entidades ────────────────────────────────────────

        private static List<Facultad> MapFacultadesDto(
            IEnumerable<FacultadDto>? dtos, Dictionary<string, Guid> strGuidOut)
        {
            var result = new List<Facultad>();
            foreach (var f in dtos ?? Enumerable.Empty<FacultadDto>())
            {
                if (string.IsNullOrWhiteSpace(f.Nombre)) continue;
                var id = Guid.TryParse(f.Id, out var g) && g != Guid.Empty ? g : Guid.NewGuid();
                result.Add(new Facultad(id, f.Nombre));
                if (!string.IsNullOrWhiteSpace(f.Id)) strGuidOut[f.Id] = id;
            }
            return result;
        }

        private static List<Programa> MapProgramasDto(
            IEnumerable<ProgramaDto>? dtos,
            Dictionary<string, Guid> facStrGuid,
            Dictionary<string, Guid> strGuidOut)
        {
            var result = new List<Programa>();
            foreach (var p in dtos ?? Enumerable.Empty<ProgramaDto>())
            {
                if (string.IsNullOrWhiteSpace(p.Nombre)) continue;
                Guid facTempId;
                if (!string.IsNullOrWhiteSpace(p.FacultadId) && facStrGuid.TryGetValue(p.FacultadId, out var mfid))
                    facTempId = mfid;
                else if (Guid.TryParse(p.FacultadId, out var pfid) && pfid != Guid.Empty)
                    facTempId = pfid;
                else continue;

                var id = Guid.TryParse(p.Id, out var pg) && pg != Guid.Empty ? pg : Guid.NewGuid();
                result.Add(new Programa(id, p.Nombre, facTempId));
                if (!string.IsNullOrWhiteSpace(p.Id)) strGuidOut[p.Id] = id;
            }
            return result;
        }

        private static List<Docente> MapDocentesDto(
            IEnumerable<DocenteImportDto>? dtos, Dictionary<string, Guid> strGuidOut)
        {
            var result = new List<Docente>();
            foreach (var d in dtos ?? Enumerable.Empty<DocenteImportDto>())
            {
                if (string.IsNullOrWhiteSpace(d.Nombre)) continue;
                var id     = Guid.TryParse(d.Id, out var dg) && dg != Guid.Empty ? dg : Guid.NewGuid();
                var correo = $"{NormalizadorTexto.Normalizar(d.Nombre).Replace(" ", ".")}@soea.local";
                var docente = new Docente(id, d.Nombre, "", correo, (decimal)d.MaxHoras,
                    new List<FranjaHoraria> { FranjaHoraria.Matutino, FranjaHoraria.Vespertino });
                if (!string.IsNullOrWhiteSpace(d.Cedula))
                    docente.ActualizarPersistenciaUi(d.Cedula, null);
                result.Add(docente);
                if (!string.IsNullOrWhiteSpace(d.Id)) strGuidOut[d.Id] = id;
            }
            return result;
        }

        private static List<Espacio> MapEspaciosDto(IEnumerable<EspacioImportDto>? dtos)
        {
            var result = new List<Espacio>();
            foreach (var e in dtos ?? Enumerable.Empty<EspacioImportDto>())
            {
                if (string.IsNullOrWhiteSpace(e.Nombre)) continue;
                var tipo = e.Tipo switch
                {
                    "Laboratorio" => TipoEspacio.Laboratorio,
                    "Auditorio"   => TipoEspacio.Auditorio,
                    _             => TipoEspacio.Salon
                };
                result.Add(new Espacio(Guid.NewGuid(), e.Nombre, tipo, e.Capacidad, e.Edificio, e.Piso));
            }
            return result;
        }

        private static List<Asignatura> MapAsignaturasDto(
            IEnumerable<AsignaturaDto>? dtos,
            Dictionary<string, Guid> progStrGuid,
            Dictionary<string, Guid> docStrGuid,
            Dictionary<string, Guid> strGuidOut)
        {
            var result = new List<Asignatura>();
            foreach (var a in dtos ?? Enumerable.Empty<AsignaturaDto>())
            {
                if (string.IsNullOrWhiteSpace(a.Nombre)) continue;

                Guid progTempId;
                if (!string.IsNullOrWhiteSpace(a.ProgramaId) && progStrGuid.TryGetValue(a.ProgramaId, out var mpid))
                    progTempId = mpid;
                else if (Guid.TryParse(a.ProgramaId, out var ppid) && ppid != Guid.Empty)
                    progTempId = ppid;
                else continue;

                var id = Guid.TryParse(a.Id, out var ag) && ag != Guid.Empty ? ag : Guid.NewGuid();
                var entidad = new Asignatura(id, a.Nombre, a.Codigo ?? string.Empty,
                    a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, progTempId);

                if (Enum.TryParse<TipoAlternancia>(a.Alternancia, ignoreCase: true, out var altP)
                    && altP != TipoAlternancia.SinAlternancia)
                    entidad.EstablecerAlternancia(altP);

                Guid? docTempId = null;
                if (!string.IsNullOrWhiteSpace(a.DocenteId))
                {
                    if (docStrGuid.TryGetValue(a.DocenteId, out var mdid))
                        docTempId = mdid;
                    else if (Guid.TryParse(a.DocenteId, out var pdid) && pdid != Guid.Empty)
                        docTempId = pdid;
                }
                entidad.AsignarDocente(docTempId);

                result.Add(entidad);
                if (!string.IsNullOrWhiteSpace(a.Id)) strGuidOut[a.Id] = id;
            }
            return result;
        }

        private static List<Grupo> MapGruposDeAsignaturas(
            IEnumerable<AsignaturaDto>? dtos,
            Dictionary<string, Guid> progStrGuid,
            Dictionary<string, Guid> asigStrGuid,
            Dictionary<string, Guid> strGuidOut)
        {
            var result = new List<Grupo>();
            foreach (var a in dtos ?? Enumerable.Empty<AsignaturaDto>())
            {
                if (string.IsNullOrWhiteSpace(a.Nombre)) continue;

                Guid progTempId;
                if (!string.IsNullOrWhiteSpace(a.ProgramaId) && progStrGuid.TryGetValue(a.ProgramaId, out var mpid))
                    progTempId = mpid;
                else if (Guid.TryParse(a.ProgramaId, out var ppid) && ppid != Guid.Empty)
                    progTempId = ppid;
                else continue;

                var numGrupo  = a.GrupoNumero.GetValueOrDefault(1);
                var groupName = $"{a.Nombre} - Grupo {numGrupo}";
                var groupAlt  = Enum.TryParse<TipoAlternancia>(a.Alternancia, ignoreCase: true, out var ga)
                    ? ga : TipoAlternancia.SinAlternancia;
                var tempGroupId = Guid.NewGuid();

                result.Add(new Grupo(tempGroupId, groupName, progTempId, 1, 30, groupAlt));

                var tempKey = BuildGroupTempKey(a.Id, numGrupo);
                strGuidOut[tempKey] = tempGroupId;
            }
            return result;
        }

        private static List<Sesion> MapSesionesDto(IEnumerable<SesionImportDto>? dtos)
        {
            var result = new List<Sesion>();
            foreach (var s in dtos ?? Enumerable.Empty<SesionImportDto>())
            {
                if (s.BloqueTiempoId == Guid.Empty) continue;
                var alt = Enum.TryParse<TipoAlternancia>(s.Alternancia, out var parsed)
                    ? parsed : TipoAlternancia.SinAlternancia;
                result.Add(new Sesion(Guid.NewGuid(), s.AsignaturaId, s.DocenteId,
                    s.BloqueTiempoId, s.EspacioId, s.GrupoId,
                    alt, Modalidad.Presencial, s.DuracionHoras,
                    esBloque: false, estaDividida: false));
            }
            return result;
        }

        private static List<MappingDto> MapearIdList(
            Dictionary<string, Guid> strGuidMap,
            Dictionary<Guid, Guid> tempRealMap)
            => strGuidMap
                .Select(kvp => new MappingDto
                {
                    TempId = kvp.Key,
                    NewId  = tempRealMap.TryGetValue(kvp.Value, out var r) ? r.ToString() : kvp.Value.ToString()
                })
                .ToList();

        private static string BuildGroupTempKey(string? asignaturaTempId, int grupoNumero)
        {
            var left = string.IsNullOrWhiteSpace(asignaturaTempId) ? "no-id" : asignaturaTempId;
            return string.Create(CultureInfo.InvariantCulture, $"{left}:{grupoNumero}");
        }
    }
}
