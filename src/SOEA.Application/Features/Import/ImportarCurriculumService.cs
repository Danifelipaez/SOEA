using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Application.Features.Import
{
    /// <summary>
    /// Persiste un <see cref="CurriculumExcelResult"/> completo dentro de una transacción:
    /// facultades → programas → docentes → espacios → asignaturas → grupos → sesiones predefinidas.
    /// Ambos endpoints de importación (Excel y JSON) delegan en este servicio.
    /// </summary>
    public class ImportarCurriculumService
    {
        private readonly IUnitOfWork _uow;
        private readonly IFacultadRepositorio _facultades;
        private readonly IProgramaRepositorio _programas;
        private readonly IDocenteRepositorio _docentes;
        private readonly IEspacioRepositorio _espacios;
        private readonly IAsignaturaRepositorio _asignaturas;
        private readonly IGrupoRepositorio _grupos;
        private readonly ISesionRepositorio _sesiones;
        private readonly IBloqueTiempoRepositorio _bloques;

        public ImportarCurriculumService(
            IUnitOfWork uow,
            IFacultadRepositorio facultades,
            IProgramaRepositorio programas,
            IDocenteRepositorio docentes,
            IEspacioRepositorio espacios,
            IAsignaturaRepositorio asignaturas,
            IGrupoRepositorio grupos,
            ISesionRepositorio sesiones,
            IBloqueTiempoRepositorio bloques)
        {
            _uow        = uow;
            _facultades = facultades;
            _programas  = programas;
            _docentes   = docentes;
            _espacios   = espacios;
            _asignaturas = asignaturas;
            _grupos     = grupos;
            _sesiones   = sesiones;
            _bloques    = bloques;
        }

        public async Task<ImportarCurriculumStats> EjecutarAsync(CurriculumExcelResult resultado)
        {
            var stats           = new ImportarCurriculumStats();
            var facultadIdMap   = new Dictionary<Guid, Guid>();
            var programaIdMap   = new Dictionary<Guid, Guid>();
            var docenteIdMap    = new Dictionary<Guid, Guid>();
            var asignaturaIdMap = new Dictionary<Guid, Guid>();
            var grupoIdMap      = new Dictionary<Guid, Guid>();

            await _uow.BeginTransactionAsync();
            try
            {
                // ── Facultades ────────────────────────────────────────────────────────
                foreach (var f in resultado.Facultades)
                {
                    var existe = await _facultades.GetByNombreAsync(f.Nombre);
                    if (existe == null)
                    {
                        var nueva = new Facultad(Guid.NewGuid(), f.Nombre);
                        _uow.Track(nueva);
                        facultadIdMap[f.Id] = nueva.Id;
                        stats.FacultadesCreadas++;
                    }
                    else
                    {
                        facultadIdMap[f.Id] = existe.Id;
                    }
                }
                // SaveAsync entre facultades y programas: los IDs reales son FK de programas
                await _uow.SaveAsync();

                // ── Programas ─────────────────────────────────────────────────────────
                foreach (var p in resultado.Programas)
                {
                    var facRealId = facultadIdMap.TryGetValue(p.FacultadId, out var fid) ? fid : p.FacultadId;
                    var existe = await _programas.GetByNombreYFacultadAsync(p.Nombre, facRealId);
                    if (existe == null)
                    {
                        var nuevo = new Programa(Guid.NewGuid(), p.Nombre, facRealId);
                        _uow.Track(nuevo);
                        programaIdMap[p.Id] = nuevo.Id;
                        stats.ProgramasCreados++;
                    }
                    else
                    {
                        programaIdMap[p.Id] = existe.Id;
                    }
                }
                await _uow.SaveAsync();

                // ── Docentes (con bloques de disponibilidad) ──────────────────────────
                // Cargamos todos para comparar nombres normalizados y evitar duplicados por acento.
                var docentesExistentes = await _docentes.GetAllAsync(); // incluye BloquesDisponibles
                var docentesNormDict = docentesExistentes
                    .GroupBy(x => NormalizadorTexto.Normalizar(x.Nombre))
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var d in resultado.Docentes)
                {
                    var nombreNorm = NormalizadorTexto.Normalizar(d.Nombre);
                    docentesNormDict.TryGetValue(nombreNorm, out var existe);

                    if (existe == null)
                    {
                        var correoFinal = string.IsNullOrWhiteSpace(d.Correo)
                            ? $"{NormalizadorTexto.Normalizar(d.Nombre).Replace(" ", ".")}@soea.local"
                            : d.Correo;
                        var nuevo = new Docente(Guid.NewGuid(), d.Nombre, d.Apellido,
                            correoFinal, d.MaximoHorasSemanales, d.Disponibilidad.ToList());

                        // Adjuntar bloques del catálogo (real BloqueTiempoId ya resuelto por el lector)
                        foreach (var bloque in d.BloquesDisponibles)
                        {
                            var bloqueTracked = await _bloques.GetByIdAsync(bloque.Id);
                            if (bloqueTracked != null) nuevo.AgregarBloqueDisponibilidad(bloqueTracked);
                        }

                        if (d.CedulaIdentidad != null)
                            nuevo.ActualizarPersistenciaUi(d.CedulaIdentidad, null);

                        _uow.Track(nuevo);
                        docenteIdMap[d.Id] = nuevo.Id;
                        stats.DocentesCreados++;
                    }
                    else
                    {
                        // Actualizar datos editables (nombre, apellido, máx. horas). El correo solo se
                        // sobreescribe si el import trae uno real (no pisamos el existente con el dummy).
                        var correoActualizado = string.IsNullOrWhiteSpace(d.Correo) ? existe.Correo : d.Correo;
                        existe.ActualizarDatos(d.Nombre, d.Apellido, correoActualizado, d.MaximoHorasSemanales);

                        // Agregar bloques nuevos sin duplicar
                        foreach (var bloque in d.BloquesDisponibles)
                        {
                            if (!existe.BloquesDisponibles.Any(b => b.Id == bloque.Id))
                            {
                                var bloqueTracked = await _bloques.GetByIdAsync(bloque.Id);
                                if (bloqueTracked != null) existe.AgregarBloqueDisponibilidad(bloqueTracked);
                            }
                        }
                        if (d.CedulaIdentidad != null)
                            existe.ActualizarPersistenciaUi(d.CedulaIdentidad, existe.DisponibilidadUiJson);

                        docenteIdMap[d.Id] = existe.Id;
                        stats.DocentesActualizados++;
                    }
                }
                await _uow.SaveAsync();

                // ── Espacios ──────────────────────────────────────────────────────────
                foreach (var e in resultado.Espacios)
                {
                    var existe = await _espacios.GetByNombreAsync(e.Nombre);
                    if (existe == null)
                    {
                        _uow.Track(new Espacio(Guid.NewGuid(), e.Nombre, e.Tipo, e.Capacidad, e.Edificio, e.Piso));
                        stats.EspaciosCreados++;
                    }
                    else
                    {
                        existe.ActualizarDatos(e.Nombre, e.Tipo, e.Edificio, e.Piso);
                        // El dominio exige capacidad > 0: solo se pisa si el import trae un valor válido.
                        if (e.Capacidad > 0)
                            existe.ActualizarCapacidad(e.Capacidad);
                        stats.EspaciosActualizados++;
                    }
                }
                await _uow.SaveAsync();

                // Lookup asignaturaId (temp) → EspacioId de su primera sesión (HC-S05: espacio fijo)
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

                    Guid? espacioFijoRealId = null;
                    if (espacioPorAsignatura.TryGetValue(a.Id, out var espacioTempId))
                    {
                        var espNombre = resultado.Espacios.FirstOrDefault(e => e.Id == espacioTempId)?.Nombre;
                        if (!string.IsNullOrWhiteSpace(espNombre))
                        {
                            var espBd = await _espacios.GetByNombreAsync(espNombre);
                            espacioFijoRealId = espBd?.Id;
                        }
                    }

                    // Buscar por código+programa primero; si no, por nombre+programa
                    var existe = await _asignaturas.GetByCodigoYProgramaAsync(a.Codigo, progRealId);
                    if (existe == null)
                        existe = await _asignaturas.GetByNombreYProgramaAsync(a.Nombre, progRealId);

                    if (existe == null)
                    {
                        var nueva = new Asignatura(Guid.NewGuid(), a.Nombre, a.Codigo,
                            a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, progRealId);
                        if (a.Alternancia != TipoAlternancia.SinAlternancia)
                            nueva.EstablecerAlternancia(a.Alternancia);
                        nueva.AsignarDocente(docenteRealId);
                        nueva.AsignarEspacioFijo(espacioFijoRealId);
                        _uow.Track(nueva);
                        asignaturaIdMap[a.Id] = nueva.Id;
                        stats.AsignaturasCreadas++;
                    }
                    else
                    {
                        // Actualizar datos editables (nombre, código, duración): sin esto las ediciones
                        // de la UI y los cambios del Excel se descartaban en silencio aunque la
                        // estadística reportara "actualizadas".
                        // Alternancia: igual que antes, un SinAlternancia entrante no pisa un tipo ya
                        // establecido (puede ser un override manual de la coordinadora).
                        var alternanciaFinal = a.Alternancia != TipoAlternancia.SinAlternancia
                            ? a.Alternancia
                            : existe.Alternancia;
                        existe.ActualizarDatos(a.Nombre, a.Codigo, a.HorasPorSesion,
                            a.SesionesPorSemana, a.SesionesLaboratorioSemestre, progRealId,
                            alternanciaFinal);
                        existe.AsignarDocente(docenteRealId);
                        existe.AsignarEspacioFijo(espacioFijoRealId);
                        asignaturaIdMap[a.Id] = existe.Id;
                        stats.AsignaturasActualizadas++;
                    }
                }
                await _uow.SaveAsync();

                // ── Grupos ────────────────────────────────────────────────────────────
                foreach (var g in resultado.Grupos)
                {
                    var progRealId = programaIdMap.TryGetValue(g.ProgramaId, out var pid2) ? pid2 : g.ProgramaId;
                    var existe = await _grupos.GetByNombreYProgramaAsync(g.Nombre, progRealId);
                    if (existe == null)
                    {
                        var nuevo = new Grupo(Guid.NewGuid(), g.Nombre, progRealId, 1, 30, g.Alternancia);
                        _uow.Track(nuevo);
                        grupoIdMap[g.Id] = nuevo.Id;
                        stats.GruposCreados++;
                    }
                    else
                    {
                        grupoIdMap[g.Id] = existe.Id;
                    }
                }
                await _uow.SaveAsync();

                // ── Sesiones predefinidas ─────────────────────────────────────────────
                foreach (var s in resultado.SesionesPredefinidas)
                {
                    if (s.BloqueTiempoId == Guid.Empty) continue;

                    var asigRealId = asignaturaIdMap.TryGetValue(s.AsignaturaId, out var asid) ? asid : s.AsignaturaId;
                    // CR-02: Sesion.DocenteId es nullable; las sesiones del curriculum traen docente.
                    var docRealId  = s.DocenteId is Guid did
                        ? (docenteIdMap.TryGetValue(did, out var sdid) ? sdid : did)
                        : Guid.Empty;

                    if (await _asignaturas.GetByIdAsync(asigRealId) == null) continue;
                    if (await _sesiones.ExisteAsync(asigRealId, docRealId, s.BloqueTiempoId)) continue;

                    var grupoRealId = s.GrupoId.HasValue && grupoIdMap.TryGetValue(s.GrupoId.Value, out var gid)
                        ? gid : s.GrupoId;

                    _uow.Track(new Sesion(
                        Guid.NewGuid(), asigRealId, docRealId,
                        s.BloqueTiempoId, s.EspacioId, grupoRealId,
                        s.Alternancia, Modalidad.Presencial, s.DuracionHoras,
                        esBloque: false, estaDividida: false));
                    stats.SesionesPersistidas++;
                }
                await _uow.SaveAsync();

                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            // Post-transacción: estadísticas adicionales
            stats.AsignaturasSinDocente = resultado.Asignaturas.Count(a => !a.DocenteId.HasValue);
            stats.Advertencias.AddRange(resultado.Advertencias);

            var todosDocentes = await _docentes.GetAllAsync();
            var gruposDup = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(
                todosDocentes.Select(d => (d.Id, d.Nombre)));
            foreach (var grupo in gruposDup)
                stats.Advertencias.Add(
                    "Posibles docentes duplicados (revisar/unificar): " +
                    string.Join(" | ", grupo.Select(g => g.Nombre)));

            stats.FacultadMappings   = facultadIdMap;
            stats.ProgramaMappings   = programaIdMap;
            stats.AsignaturaMappings = asignaturaIdMap;
            stats.DocenteMappings    = docenteIdMap;
            stats.GrupoMappings      = grupoIdMap;

            return stats;
        }
    }
}
