using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Enums;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;
using SOEA.Infrastructure.Data.Context;
using System.Globalization;

namespace SOEA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly SOEABdContext _context;
        private readonly ILectorExcel _lectorExcel;

        public ImportController(SOEABdContext context, ILectorExcel lectorExcel)
        {
            _context = context;
            _lectorExcel = lectorExcel;
        }

        /// <summary>
        /// Importa el Excel de horario/currículum directamente desde el archivo.
        /// Detecta columnas por cabecera, normaliza nombres de docentes y persiste todo
        /// en una transacción: facultades, programas, docentes (con disponibilidad),
        /// espacios, asignaturas, grupos y sesiones predefinidas con BloqueTiempoId real.
        /// </summary>
        [HttpPost("excel")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportarExcel(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return BadRequest("No se recibió ningún archivo.");

            if (!archivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !archivo.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                return BadRequest("El archivo debe ser .xlsx o .xls.");

            // Cargar catálogo de bloques seeded (1-hora, Lun-Vie 06-22h, Sáb 06-13h)
            var bloquesCatalogo = await _context.BloqueTiempos
                .AsNoTracking()
                .ToListAsync();

            // Agrupar por (Dia, HoraInicio) para lookup O(1)
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

            var stats = new ImportExcelStatsDto();
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var facultadIdMap  = new Dictionary<Guid, Guid>();
                var programaIdMap  = new Dictionary<Guid, Guid>();
                var docenteIdMap   = new Dictionary<Guid, Guid>();
                var asignaturaIdMap = new Dictionary<Guid, Guid>();

                // ── Facultades ────────────────────────────────────────────────────────
                foreach (var f in resultado.Facultades)
                {
                    var existe = await _context.Facultades
                        .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, f.Nombre));
                    if (existe == null)
                    {
                        var nueva = new Facultad(Guid.NewGuid(), f.Nombre);
                        _context.Facultades.Add(nueva);
                        facultadIdMap[f.Id] = nueva.Id;
                        stats.FacultadesCreadas++;
                    }
                    else
                    {
                        facultadIdMap[f.Id] = existe.Id;
                    }
                }
                // SaveChanges entre facultades y programas: necesitamos los IDs reales de FK
                await _context.SaveChangesAsync();

                // ── Programas ─────────────────────────────────────────────────────────
                foreach (var p in resultado.Programas)
                {
                    var facRealId = facultadIdMap.TryGetValue(p.FacultadId, out var fid) ? fid : p.FacultadId;
                    var existe = await _context.Programas
                        .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, p.Nombre) && x.FacultadId == facRealId);
                    if (existe == null)
                    {
                        var nuevo = new Programa(Guid.NewGuid(), p.Nombre, facRealId);
                        _context.Programas.Add(nuevo);
                        programaIdMap[p.Id] = nuevo.Id;
                        stats.ProgramasCreados++;
                    }
                    else
                    {
                        programaIdMap[p.Id] = existe.Id;
                    }
                }
                await _context.SaveChangesAsync();

                // ── Docentes (con disponibilidad) ─────────────────────────────────────
                // Cargamos todos los docentes en memoria para comparar nombres normalizados
                // (sin tildes, case-insensitive) y evitar duplicados por variantes de acento.
                var docentesExistentes = await _context.Docentes
                    .Include(x => x.BloquesDisponibles)
                    .ToListAsync();
                var docentesNormDict = docentesExistentes
                    .GroupBy(x => NormalizadorTexto.Normalizar(x.Nombre))
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var d in resultado.Docentes)
                {
                    var nombreNorm = NormalizadorTexto.Normalizar(d.Nombre);
                    docentesNormDict.TryGetValue(nombreNorm, out var existe);

                    if (existe == null)
                    {
                        // Correo determinista derivado del nombre cuando no viene en el Excel
                        var correoFinal = string.IsNullOrWhiteSpace(d.Correo)
                            ? $"{NormalizadorTexto.Normalizar(d.Nombre).Replace(" ", ".")}@soea.local"
                            : d.Correo;
                        var nuevo = new Docente(Guid.NewGuid(), d.Nombre, d.Apellido,
                            correoFinal, d.MaximoHorasSemanales, d.Disponibilidad.ToList());

                        // Adjuntar bloques del catálogo (ya existen en BD)
                        foreach (var bloque in d.BloquesDisponibles)
                        {
                            var bloqueTracked = await _context.BloqueTiempos.FindAsync(bloque.Id);
                            if (bloqueTracked != null) nuevo.AgregarBloqueDisponibilidad(bloqueTracked);
                        }

                        _context.Docentes.Add(nuevo);
                        docenteIdMap[d.Id] = nuevo.Id;
                        stats.DocentesCreados++;
                    }
                    else
                    {
                        // Actualizar disponibilidad: agregar bloques nuevos sin duplicar
                        foreach (var bloque in d.BloquesDisponibles)
                        {
                            if (!existe.BloquesDisponibles.Any(b => b.Id == bloque.Id))
                            {
                                var bloqueTracked = await _context.BloqueTiempos.FindAsync(bloque.Id);
                                if (bloqueTracked != null) existe.AgregarBloqueDisponibilidad(bloqueTracked);
                            }
                        }
                        docenteIdMap[d.Id] = existe.Id;
                        stats.DocentesActualizados++;
                    }
                }
                await _context.SaveChangesAsync();

                // ── Espacios ──────────────────────────────────────────────────────────
                foreach (var e in resultado.Espacios)
                {
                    var existe = await _context.Espacios
                        .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, e.Nombre));
                    if (existe == null)
                    {
                        _context.Espacios.Add(new Espacio(Guid.NewGuid(), e.Nombre, e.Tipo, e.Capacidad, e.Edificio, e.Piso));
                        stats.EspaciosCreados++;
                    }
                }
                await _context.SaveChangesAsync();

                // Lookup: asignaturaId (temp) → EspacioId de su primera sesión predefinida.
                // Esto permite propagar HC-S05: el espacio del curriculum se fija en la entidad.
                var espacioPorAsignatura = resultado.SesionesPredefinidas
                    .Where(s => s.EspacioId.HasValue)
                    .GroupBy(s => s.AsignaturaId)
                    .ToDictionary(g => g.Key, g => g.First().EspacioId!.Value);

                // ── Asignaturas ───────────────────────────────────────────────────────
                foreach (var a in resultado.Asignaturas)
                {
                    var progRealId = programaIdMap.TryGetValue(a.ProgramaId, out var pid) ? pid : a.ProgramaId;
                    var docenteRealId = a.DocenteId.HasValue && docenteIdMap.TryGetValue(a.DocenteId.Value, out var did)
                        ? did : a.DocenteId;

                    // Resolver el espacio fijo: buscar en el catálogo BD por nombre (los espacios
                    // ya se persistieron en la sección anterior con sus IDs reales).
                    Guid? espacioFijoRealId = null;
                    if (espacioPorAsignatura.TryGetValue(a.Id, out var espacioTempId))
                    {
                        var espNombreTemp = resultado.Espacios.FirstOrDefault(e => e.Id == espacioTempId)?.Nombre;
                        if (!string.IsNullOrWhiteSpace(espNombreTemp))
                        {
                            var espBd = await _context.Espacios
                                .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, espNombreTemp));
                            espacioFijoRealId = espBd?.Id;
                        }
                    }

                    var existe = await _context.Asignaturas
                        .FirstOrDefaultAsync(x => x.Codigo == a.Codigo && x.ProgramaId == progRealId);
                    if (existe == null)
                    {
                        existe = await _context.Asignaturas
                            .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, a.Nombre) && x.ProgramaId == progRealId && x.DocenteId == docenteRealId);
                    }

                    if (existe == null)
                    {
                        var nueva = new Asignatura(Guid.NewGuid(), a.Nombre, a.Codigo,
                            a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, progRealId);
                        // Override manual del tipo si el import lo trae explícito (TipoA/TipoB);
                        // si viene SinAlternancia (default) se conserva la inferencia por umbral.
                        if (a.Alternancia != TipoAlternancia.SinAlternancia)
                            nueva.EstablecerAlternancia(a.Alternancia);
                        nueva.AsignarDocente(docenteRealId);
                        nueva.AsignarEspacioFijo(espacioFijoRealId);
                        _context.Asignaturas.Add(nueva);
                        asignaturaIdMap[a.Id] = nueva.Id;
                        stats.AsignaturasCreadas++;
                    }
                    else
                    {
                        existe.AsignarDocente(docenteRealId);
                        existe.AsignarEspacioFijo(espacioFijoRealId);
                        // Re-aplicar alternancia inferida por el lector; si es TipoA/TipoB
                        // sobreescribe el valor antiguo (p.ej. SinAlternancia de imports anteriores).
                        if (a.Alternancia != TipoAlternancia.SinAlternancia)
                            existe.EstablecerAlternancia(a.Alternancia);
                        asignaturaIdMap[a.Id] = existe.Id;
                        stats.AsignaturasActualizadas++;
                    }
                }
                await _context.SaveChangesAsync();

                // ── Grupos ────────────────────────────────────────────────────────────
                var grupoIdMap = new Dictionary<Guid, Guid>();
                foreach (var g in resultado.Grupos)
                {
                    var progRealId = programaIdMap.TryGetValue(g.ProgramaId, out var pid2) ? pid2 : g.ProgramaId;
                    var existe = await _context.Grupos
                        .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, g.Nombre) && x.ProgramaId == progRealId);
                    if (existe == null)
                    {
                        var nuevo = new Grupo(Guid.NewGuid(), g.Nombre, progRealId, 1, 30, g.Alternancia);
                        _context.Grupos.Add(nuevo);
                        grupoIdMap[g.Id] = nuevo.Id;
                        stats.GruposCreados++;
                    }
                    else
                    {
                        grupoIdMap[g.Id] = existe.Id;
                    }
                }
                await _context.SaveChangesAsync();

                // ── Sesiones predefinidas ─────────────────────────────────────────────
                foreach (var s in resultado.SesionesPredefinidas)
                {
                    if (s.BloqueTiempoId == Guid.Empty) continue; // sin día/hora → se omite

                    var asigRealId = asignaturaIdMap.TryGetValue(s.AsignaturaId, out var asid) ? asid : s.AsignaturaId;
                    var docRealId  = docenteIdMap.TryGetValue(s.DocenteId, out var sdid) ? sdid : s.DocenteId;

                    var asig = await _context.Asignaturas.FindAsync(asigRealId);
                    if (asig == null) continue;

                    // Evitar duplicados exactos (misma asig+docente+bloque)
                    bool yaExiste = await _context.Sesiones.AnyAsync(x =>
                        x.AsignaturaId == asigRealId &&
                        x.DocenteId    == docRealId  &&
                        x.BloqueTiempoId == s.BloqueTiempoId);
                    if (yaExiste) continue;

                    var grupoRealId = s.GrupoId.HasValue && grupoIdMap.TryGetValue(s.GrupoId.Value, out var gid)
                        ? gid : s.GrupoId;
                    var sesion = new Sesion(
                        Guid.NewGuid(), asigRealId, docRealId,
                        s.BloqueTiempoId, s.EspacioId, grupoRealId,
                        s.Alternancia, Modalidad.Presencial, s.DuracionHoras,
                        esBloque: false, estaDividida: false);
                    _context.Sesiones.Add(sesion);
                    stats.SesionesPersistidas++;
                }
                await _context.SaveChangesAsync();

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, $"Error al persistir: {ex.Message}");
            }

            stats.AsignaturasSinDocente = resultado.Asignaturas.Count(a => !a.DocenteId.HasValue);
            stats.Advertencias = resultado.Advertencias.ToList();

            // Reporte de posibles docentes duplicados (mismo profesor con variantes de nombre →
            // se fragmenta en varios registros y el motor los agenda en paralelo). No se fusionan
            // automáticamente: se avisa para que el usuario los revise/unifique manualmente.
            var todosDocentes = await _context.Docentes
                .Select(d => new { d.Id, d.Nombre })
                .ToListAsync();
            var gruposDup = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(
                todosDocentes.Select(d => new DetectorDocentesDuplicados.Docente(d.Id, d.Nombre)));
            foreach (var grupo in gruposDup)
                stats.Advertencias.Add(
                    "Posibles docentes duplicados (revisar/unificar): " +
                    string.Join(" | ", grupo.Select(g => g.Nombre)));

            return Ok(stats);
        }

        [HttpGet("/api/facultades")]
        public async Task<IActionResult> GetFacultades()
            => Ok(await _context.Facultades
                .OrderBy(f => f.Nombre)
                .Select(f => new { id = f.Id.ToString(), nombre = f.Nombre })
                .ToListAsync());

        [HttpGet("/api/programas")]
        public async Task<IActionResult> GetProgramas()
            => Ok(await _context.Programas
                .OrderBy(p => p.Nombre)
                .Select(p => new { id = p.Id.ToString(), nombre = p.Nombre, facultadId = p.FacultadId.ToString() })
                .ToListAsync());

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
                    var exists = await _context.Facultades.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, f.Nombre));
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

                    var fac = await _context.Facultades.FindAsync(facultadRealId);
                    if (fac == null) continue;

                    var exists = await _context.Programas.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, p.Nombre) && x.FacultadId == fac.Id);
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

                    // Buscar por ID real primero, luego por nombre
                    Docente? exists = null;
                    if (!string.IsNullOrWhiteSpace(d.Id) && Guid.TryParse(d.Id, out var dGuid) && dGuid != Guid.Empty)
                        exists = await _context.Docentes.FindAsync(dGuid);
                    exists ??= await _context.Docentes.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, d.Nombre));

                    Guid docenteTargetId;
                    if (exists == null)
                    {
                        var id = Guid.NewGuid();
                        var correo = $"{NormalizadorTexto.Normalizar(d.Nombre).Replace(" ", ".")}@soea.local";
                        var docente = new Docente(id, d.Nombre, "", correo,
                            (decimal)d.MaxHoras,
                            new List<Domain.Enums.FranjaHoraria> { Domain.Enums.FranjaHoraria.Matutino, Domain.Enums.FranjaHoraria.Vespertino });
                        if (!string.IsNullOrWhiteSpace(d.Cedula))
                            docente.ActualizarPersistenciaUi(d.Cedula, null);
                        _context.Docentes.Add(docente);
                        docenteTargetId = id;
                    }
                    else
                    {
                        exists.ActualizarDatos(d.Nombre, "", exists.Correo, (decimal)d.MaxHoras);
                        if (!string.IsNullOrWhiteSpace(d.Cedula))
                            exists.ActualizarPersistenciaUi(d.Cedula, exists.DisponibilidadUiJson);
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
                    var exists = await _context.Espacios.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, e.Nombre));
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

                    var prog = await _context.Programas.FindAsync(programaRealId);
                    if (prog == null) continue;

                    Guid? docenteRealId = null;
                    if (!string.IsNullOrWhiteSpace(a.DocenteId))
                    {
                        if (docenteIdMap.TryGetValue(a.DocenteId, out var mappedDocId))
                            docenteRealId = mappedDocId;
                        else if (Guid.TryParse(a.DocenteId, out var docGuid))
                            docenteRealId = docGuid;
                    }

                    // Buscar por ID real primero (asignatura cargada desde BD), luego por nombre+programa
                    Asignatura? exists = null;
                    if (Guid.TryParse(a.Id, out var aGuid) && aGuid != Guid.Empty)
                        exists = await _context.Asignaturas.FindAsync(aGuid);
                    exists ??= await _context.Asignaturas.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, a.Nombre) && x.ProgramaId == prog.Id);

                    // Override manual del tipo si el import lo trae explícito (TipoA/TipoB);
                    // SinAlternancia (default) deja que la inferencia por umbral decida.
                    TipoAlternancia? altExplicita =
                        Enum.TryParse<TipoAlternancia>(a.Alternancia, ignoreCase: true, out var altP) &&
                        altP != TipoAlternancia.SinAlternancia
                            ? altP : null;

                    Guid targetId;
                    if (exists == null)
                    {
                        var entidad = new Asignatura(Guid.NewGuid(), a.Nombre, a.Codigo ?? string.Empty, a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, prog.Id);
                        if (altExplicita.HasValue) entidad.EstablecerAlternancia(altExplicita.Value);
                        entidad.AsignarDocente(docenteRealId);
                        _context.Asignaturas.Add(entidad);
                        targetId = entidad.Id;
                    }
                    else
                    {
                        exists.ActualizarDatos(
                            a.Nombre,
                            a.Codigo,
                            a.HorasPorSesion,
                            a.SesionesPorSemana,
                            a.SesionesLaboratorioSemestre,
                            prog.Id,
                            altExplicita);
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

                    var asign = await _context.Asignaturas.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, a.Nombre) && x.ProgramaId == programaRealId);
                    if (asign == null) continue;

                    var numeroGrupo = a.GrupoNumero.GetValueOrDefault(1);
                    var groupName = $"{a.Nombre} - Grupo {numeroGrupo}";

                    var existente = await _context.Grupos.FirstOrDefaultAsync(g =>
                        EF.Functions.ILike(g.Nombre, groupName) &&
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
                    var asign = await _context.Asignaturas.FindAsync(s.AsignaturaId);
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

}
