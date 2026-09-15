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
    /// facultades → programas → docentes → espacios → asignaturas → grupos.
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
        private readonly IBloqueTiempoRepositorio _bloques;

        public ImportarCurriculumService(
            IUnitOfWork uow,
            IFacultadRepositorio facultades,
            IProgramaRepositorio programas,
            IDocenteRepositorio docentes,
            IEspacioRepositorio espacios,
            IAsignaturaRepositorio asignaturas,
            IGrupoRepositorio grupos,
            IBloqueTiempoRepositorio bloques)
        {
            _uow        = uow;
            _facultades = facultades;
            _programas  = programas;
            _docentes   = docentes;
            _espacios   = espacios;
            _asignaturas = asignaturas;
            _grupos     = grupos;
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
                // PERF2 auditoría: GetByNombreAsync consulta la BD, no el change tracker — un
                // Add() de EF Core no es visible para una consulta LINQ hasta el próximo
                // SaveChangesAsync. Dos filas del Excel con la misma facultad, sin la flush por
                // iteración que esto tenía antes, se habrían creado dos veces. En vez de eso
                // (un SaveAsync por fila — el costo real de PERF2), se resuelve la dedup con un
                // diccionario en memoria (mismo patrón que docentesNormDict más abajo) sembrado
                // con una sola carga inicial, y se guarda todo junto al final del método.
                var facultadesPorNombre = (await _facultades.GetAllAsync())
                    .GroupBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var f in resultado.Facultades)
                {
                    if (!facultadesPorNombre.TryGetValue(f.Nombre, out var existe))
                    {
                        var nueva = new Facultad(Guid.NewGuid(), f.Nombre);
                        _uow.Track(nueva);
                        facultadesPorNombre[f.Nombre] = nueva;
                        facultadIdMap[f.Id] = nueva.Id;
                        stats.FacultadesCreadas++;
                    }
                    else
                    {
                        facultadIdMap[f.Id] = existe.Id;
                    }
                }

                // ── Programas ─────────────────────────────────────────────────────────
                // PERF2 auditoría: mismo cambio que Facultades — dict en memoria en vez de
                // GetByNombreYFacultadAsync + SaveAsync por fila.
                var programasPorClave = (await _programas.GetAllAsync())
                    .ToDictionary(x => (x.Nombre.ToUpperInvariant(), x.FacultadId));
                foreach (var p in resultado.Programas)
                {
                    var facRealId = facultadIdMap.TryGetValue(p.FacultadId, out var fid) ? fid : p.FacultadId;
                    var clave = (p.Nombre.ToUpperInvariant(), facRealId);
                    if (!programasPorClave.TryGetValue(clave, out var existe))
                    {
                        var nuevo = new Programa(Guid.NewGuid(), p.Nombre, facRealId);
                        _uow.Track(nuevo);
                        programasPorClave[clave] = nuevo;
                        programaIdMap[p.Id] = nuevo.Id;
                        stats.ProgramasCreados++;
                    }
                    else
                    {
                        programaIdMap[p.Id] = existe.Id;
                    }
                }

                // ── Docentes (con bloques de disponibilidad) ──────────────────────────
                // Cargamos todos para comparar nombres normalizados y evitar duplicados por acento
                // — IDocenteRepositorio no expone una búsqueda por nombre normalizado, así que a
                // diferencia de Facultades/Programas/Espacios/Asignaturas/Grupos (que sí pueden
                // reconsultar la BD cada iteración) este diccionario vive solo en memoria.
                // Regresión (auditoría de limpieza, hallazgo 1.6): nunca se actualizaba dentro del
                // bucle — dos filas del Excel con el mismo docente (normal: un docente dicta
                // varias asignaturas) entraban las dos por la rama "no existe" y creaban dos
                // Docente distintos, justo el problema que FusionDocentesService existe para
                // limpiar después. Ahora se añade la nueva entrada al diccionario en el momento de
                // crearla, para que la siguiente fila del mismo docente la encuentre.
                var docentesExistentes = await _docentes.GetAllAsync(); // incluye BloquesDisponibles
                var docentesNormDict = docentesExistentes
                    .GroupBy(x => NormalizadorTexto.Normalizar(x.Nombre))
                    .ToDictionary(g => g.Key, g => g.First());
                // IMP1 auditoría: distingue un docente que YA estaba en BD (detached, necesita
                // UpdateAsync para emitir el UPDATE) de uno creado por una fila ANTERIOR de este
                // mismo import (todavía Added/sin guardar vía _uow.Track — llamar UpdateAsync sobre
                // él intentaría actualizar una fila que aún no existe y falla).
                var docentesCreadosEsteRun = new HashSet<Guid>();
                // PERF2 auditoría: sin esto, un Excel con 400 docentes × 15 bloques de
                // disponibilidad hacía hasta 6000 GetByIdAsync — EF ya cachea por id tras el primer
                // FindAsync (identity map), pero seguía siendo una llamada async por fila. La
                // grilla tiene ~90 bloques únicos; con este caché la 91ª consulta en adelante ya no
                // ni pasa por el repositorio.
                var bloquesCache = new Dictionary<Guid, BloqueTiempo?>();
                async Task<BloqueTiempo?> ResolverBloqueAsync(Guid id)
                {
                    if (bloquesCache.TryGetValue(id, out var cacheado)) return cacheado;
                    var bloque = await _bloques.GetByIdAsync(id);
                    bloquesCache[id] = bloque;
                    return bloque;
                }

                foreach (var d in resultado.Docentes)
                {
                    var nombreNorm = NormalizadorTexto.Normalizar(d.Nombre);
                    docentesNormDict.TryGetValue(nombreNorm, out var existe);

                    if (existe == null)
                    {
                        var nuevoId = Guid.NewGuid();
                        // DUP auditoría: NormalizadorTexto.CorreoSintetico incluye el Id — dos
                        // docentes homónimos ya no sintetizan el mismo correo (chocaba contra el
                        // índice único de Docente.Correo).
                        var correoFinal = string.IsNullOrWhiteSpace(d.Correo)
                            ? NormalizadorTexto.CorreoSintetico(d.Nombre, nuevoId)
                            : d.Correo;
                        var nuevo = new Docente(nuevoId, d.Nombre, d.Apellido,
                            correoFinal, d.MaximoHorasSemanales, d.Disponibilidad.ToList());

                        // Adjuntar bloques del catálogo (real BloqueTiempoId ya resuelto por el lector)
                        foreach (var bloque in d.BloquesDisponibles)
                        {
                            var bloqueTracked = await ResolverBloqueAsync(bloque.Id);
                            if (bloqueTracked != null) nuevo.AgregarBloqueDisponibilidad(bloqueTracked);
                        }

                        if (d.CedulaIdentidad != null)
                            nuevo.ActualizarPersistenciaUi(d.CedulaIdentidad, null);

                        _uow.Track(nuevo);
                        docentesNormDict[nombreNorm] = nuevo;
                        docentesCreadosEsteRun.Add(nuevo.Id);
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
                                var bloqueTracked = await ResolverBloqueAsync(bloque.Id);
                                if (bloqueTracked != null) existe.AgregarBloqueDisponibilidad(bloqueTracked);
                            }
                        }
                        if (d.CedulaIdentidad != null)
                            existe.ActualizarPersistenciaUi(d.CedulaIdentidad, existe.DisponibilidadUiJson);

                        // IMP1 auditoría: `existe` viene de _docentes.GetAllAsync(), el único
                        // lookup de este método que usa AsNoTracking() (DocenteRepositorio.cs) —
                        // mutarlo sin decírselo al DbContext no emitía ningún UPDATE. La respuesta
                        // reportaba "N docentes actualizados" y la fila en BD quedaba intacta.
                        // Salvo que `existe` sea un docente recién creado por una fila anterior de
                        // ESTE MISMO import: ese sigue Added (aún no guardado) y ya se actualiza
                        // solo, sin necesidad de UpdateAsync — llamarlo fallaría (no hay fila en BD
                        // todavía contra la cual emitir el UPDATE).
                        if (!docentesCreadosEsteRun.Contains(existe.Id))
                            await _docentes.UpdateAsync(existe);

                        docenteIdMap[d.Id] = existe.Id;
                        stats.DocentesActualizados++;
                    }
                }
                await _uow.SaveAsync();

                // ── Espacios ──────────────────────────────────────────────────────────
                // PERF2 auditoría: dict en memoria en vez de GetByNombreAsync + SaveAsync por
                // fila; se reutiliza más abajo en Asignaturas (que antes repetía su propia
                // consulta a _espacios por el mismo nombre).
                var espaciosPorNombre = (await _espacios.GetAllAsync())
                    .GroupBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                // IMP1-style auditoría: dos filas del Excel para el MISMO espacio dentro de esta
                // misma corrida — la primera lo crea (Added, sin guardar todavía), la segunda no
                // puede llamar UpdateAsync sobre él (no hay fila en BD contra la cual emitir el
                // UPDATE). Mismo guard que docentesCreadosEsteRun más arriba.
                var espaciosCreadosEsteRun = new HashSet<Guid>();
                foreach (var e in resultado.Espacios)
                {
                    if (!espaciosPorNombre.TryGetValue(e.Nombre, out var existe))
                    {
                        var nuevo = new Espacio(Guid.NewGuid(), e.Nombre, e.Tipo, e.Capacidad, e.Edificio, e.Piso);
                        _uow.Track(nuevo);
                        espaciosPorNombre[e.Nombre] = nuevo;
                        espaciosCreadosEsteRun.Add(nuevo.Id);
                        stats.EspaciosCreados++;
                    }
                    else
                    {
                        // L7 auditoría: antes se incrementaba el contador aunque ningún campo
                        // cambiara realmente (reimportar el mismo Excel sin editar nada reportaba
                        // "N espacios actualizados" de forma engañosa).
                        bool cambios = existe.Nombre != e.Nombre || existe.Tipo != e.Tipo ||
                            existe.Edificio != e.Edificio || existe.Piso != e.Piso;
                        existe.ActualizarDatos(e.Nombre, e.Tipo, e.Edificio, e.Piso);
                        // El dominio exige capacidad > 0: solo se pisa si el import trae un valor válido.
                        if (e.Capacidad > 0 && e.Capacidad != existe.Capacidad)
                        {
                            existe.ActualizarCapacidad(e.Capacidad);
                            cambios = true;
                        }
                        // El fetch inicial es AsNoTracking (GetAllAsync) — sin este UpdateAsync
                        // explícito la mutación de arriba no se habría persistido nunca.
                        if (cambios)
                        {
                            if (!espaciosCreadosEsteRun.Contains(existe.Id))
                                await _espacios.UpdateAsync(existe);
                            stats.EspaciosActualizados++;
                        }
                    }
                }

                // Lookup asignaturaId (temp) → EspacioId de su primera sesión (HC-S05: espacio fijo)
                var espacioPorAsignatura = resultado.SesionesPredefinidas
                    .Where(s => s.EspacioId.HasValue)
                    .GroupBy(s => s.AsignaturaId)
                    .ToDictionary(g => g.Key, g => g.First().EspacioId!.Value);

                // Requisito de laboratorio por asignatura (Id REAL, post-remap), consumido al crear
                // los grupos de esa asignatura — reemplaza a Asignatura.EspacioFijoId (ver Grupo.RequisitosEspacio).
                var espacioFijoPorAsignaturaReal = new Dictionary<Guid, Guid>();

                // ── Asignaturas ───────────────────────────────────────────────────────
                // PERF2 auditoría: una sola carga inicial en vez de dos consultas por fila
                // (código+programa, y si falla, nombre+programa) más un SaveAsync por fila.
                // Un scan lineal en memoria por fila es intrascendente frente a un round trip.
                var asignaturasExistentes = (await _asignaturas.GetAllAsync()).ToList();
                Asignatura? BuscarAsignaturaExistente(string codigo, string nombre, Guid programaId) =>
                    asignaturasExistentes.FirstOrDefault(x => x.Codigo == codigo && x.ProgramaId == programaId)
                    ?? asignaturasExistentes.FirstOrDefault(x =>
                        x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && x.ProgramaId == programaId);
                // IMP1-style auditoría: mismo guard que docentesCreadosEsteRun — dos filas del
                // Excel para la MISMA asignatura en esta corrida: la primera la crea (Added, sin
                // guardar); la segunda no puede UpdateAsync sobre una fila que aún no existe en BD.
                var asignaturasCreadasEsteRun = new HashSet<Guid>();

                foreach (var a in resultado.Asignaturas)
                {
                    var progRealId = programaIdMap.TryGetValue(a.ProgramaId, out var pid) ? pid : a.ProgramaId;

                    Guid? espacioFijoRealId = null;
                    if (espacioPorAsignatura.TryGetValue(a.Id, out var espacioTempId))
                    {
                        var espNombre = resultado.Espacios.FirstOrDefault(e => e.Id == espacioTempId)?.Nombre;
                        // Reutiliza el dict sembrado en el bucle de Espacios — antes repetía su
                        // propia consulta ILike a _espacios por el mismo nombre.
                        if (!string.IsNullOrWhiteSpace(espNombre) && espaciosPorNombre.TryGetValue(espNombre, out var espBd))
                            espacioFijoRealId = espBd.Id;
                    }

                    var existe = BuscarAsignaturaExistente(a.Codigo, a.Nombre, progRealId);

                    if (existe == null)
                    {
                        var nueva = new Asignatura(Guid.NewGuid(), a.Nombre, a.Codigo,
                            a.HorasPorSesion, a.SesionesPorSemana, a.SesionesLaboratorioSemestre, progRealId);
                        if (a.Alternancia != TipoAlternancia.SinAlternancia)
                            nueva.EstablecerAlternancia(a.Alternancia);
                        if (espacioFijoRealId.HasValue)
                            espacioFijoPorAsignaturaReal[nueva.Id] = espacioFijoRealId.Value;
                        _uow.Track(nueva);
                        asignaturasExistentes.Add(nueva);
                        asignaturasCreadasEsteRun.Add(nueva.Id);
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
                        // IMP4 auditoría: el import (Excel y JSON) no tiene columnas para
                        // teoría-virtual ni laboratorio — la Asignatura que construye el lector
                        // siempre trae esos dos en 0. La sobrecarga LEGADA de ActualizarDatos fija
                        // ambos a 0 sin condición, así que una reimportación borraba en silencio
                        // cualquier track configurado a mano vía PUT /api/asignaturas/{id}. Se usa
                        // la sobrecarga moderna preservando los valores YA EXISTENTES para los dos
                        // tracks que el import nunca pudo conocer.
                        existe.ActualizarDatos(
                            nombre: a.Nombre,
                            codigo: a.Codigo,
                            sesionesTeoriaPresencialSemana: a.SesionesTeoriaPresencialSemana,
                            horasTeoriaPresencial: a.HorasTeoriaPresencial,
                            sesionesTeoriaVirtualSemana: existe.SesionesTeoriaVirtualSemana,
                            horasTeoriaVirtual: existe.HorasTeoriaVirtual,
                            sesionesLaboratorioSemana: existe.SesionesLaboratorioSemana,
                            horasLaboratorio: existe.HorasLaboratorio,
                            sesionesLaboratorioSemestre: a.SesionesLaboratorioSemestre,
                            programaId: progRealId,
                            alternanciaExplicita: alternanciaFinal);
                        if (espacioFijoRealId.HasValue)
                            espacioFijoPorAsignaturaReal[existe.Id] = espacioFijoRealId.Value;
                        // El fetch inicial es AsNoTracking (GetAllAsync) — sin este UpdateAsync
                        // explícito ActualizarDatos de arriba no se habría persistido nunca.
                        if (!asignaturasCreadasEsteRun.Contains(existe.Id))
                            await _asignaturas.UpdateAsync(existe);
                        asignaturaIdMap[a.Id] = existe.Id;
                        stats.AsignaturasActualizadas++;
                    }
                }

                // ── Grupos (el grupo carga su asignatura y su docente — remapeados temp→real) ──
                // PERF2 auditoría: dict en memoria en vez de GetByNombreYProgramaAsync por fila
                // (el UpdateAsync condicional de la rama "existe" ya evitaba el SaveAsync
                // incondicional; solo faltaba dejar de repetir la consulta de búsqueda).
                var gruposPorClave = (await _grupos.GetAllAsync())
                    .ToDictionary(x => (x.Nombre.ToUpperInvariant(), x.ProgramaId));
                // IMP1-style auditoría: mismo guard que docentesCreadosEsteRun/asignaturasCreadasEsteRun.
                var gruposCreadosEsteRun = new HashSet<Guid>();
                foreach (var g in resultado.Grupos)
                {
                    var progRealId = programaIdMap.TryGetValue(g.ProgramaId, out var pid2) ? pid2 : g.ProgramaId;
                    Guid? asigRealId = g.AsignaturaId.HasValue && asignaturaIdMap.TryGetValue(g.AsignaturaId.Value, out var garid)
                        ? garid : g.AsignaturaId;
                    Guid? docRealId = g.DocenteId.HasValue && docenteIdMap.TryGetValue(g.DocenteId.Value, out var gdid)
                        ? gdid : g.DocenteId;
                    // M14 auditoría (descubierto al investigar la migración de FK): a diferencia de
                    // progRealId/asigRealId/docRealId, g.FacultadId se usaba SIN remapear — se
                    // persistía el id TEMPORAL asignado durante el mapeo DTO→entidad, no el real
                    // creado al persistir la Facultad. En la BD local esto dejó el 100% de los
                    // Grupos con facultad_id apuntando a nada.
                    Guid? facRealIdGrupo = g.FacultadId.HasValue && facultadIdMap.TryGetValue(g.FacultadId.Value, out var gfid)
                        ? gfid : g.FacultadId;

                    Guid? espacioFijoDelGrupo = asigRealId.HasValue &&
                        espacioFijoPorAsignaturaReal.TryGetValue(asigRealId.Value, out var espFijo) ? espFijo : null;

                    var clave = (g.Nombre.ToUpperInvariant(), progRealId);
                    if (!gruposPorClave.TryGetValue(clave, out var existe))
                    {
                        var nuevo = new Grupo(Guid.NewGuid(), g.Nombre, progRealId, 30, g.Alternancia,
                            asignaturaId: asigRealId, facultadId: facRealIdGrupo, docenteId: docRealId);
                        if (!string.IsNullOrWhiteSpace(g.DisponibilidadUiJson))
                            nuevo.ActualizarDisponibilidadUi(g.DisponibilidadUiJson);
                        if (espacioFijoDelGrupo.HasValue)
                            nuevo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
                            {
                                new(TipoSesion.Laboratorio, espacioFijoDelGrupo, TipoEspacio.Laboratorio, 0)
                            });
                        _uow.Track(nuevo);
                        gruposPorClave[clave] = nuevo;
                        gruposCreadosEsteRun.Add(nuevo.Id);
                        grupoIdMap[g.Id] = nuevo.Id;
                        stats.GruposCreados++;
                    }
                    else
                    {
                        // Grupo existente: completar el docente si viene en el import y aún no lo tiene.
                        bool cambios = false;
                        if (docRealId.HasValue && !existe.DocenteId.HasValue)
                        {
                            existe.AsignarDocente(docRealId);
                            cambios = true;
                        }
                        if (string.IsNullOrWhiteSpace(existe.DisponibilidadUiJson) &&
                            !string.IsNullOrWhiteSpace(g.DisponibilidadUiJson))
                        {
                            existe.ActualizarDisponibilidadUi(g.DisponibilidadUiJson);
                            cambios = true;
                        }
                        if (espacioFijoDelGrupo.HasValue &&
                            !existe.RequisitosEspacio.Any(r => r.TipoSesion == TipoSesion.Laboratorio))
                        {
                            existe.ActualizarRequisitosEspacio(new List<RequisitoEspacio>(existe.RequisitosEspacio)
                            {
                                new(TipoSesion.Laboratorio, espacioFijoDelGrupo, TipoEspacio.Laboratorio, 0)
                            });
                            cambios = true;
                        }
                        if (cambios && !gruposCreadosEsteRun.Contains(existe.Id))
                            await _grupos.UpdateAsync(existe);
                        grupoIdMap[g.Id] = existe.Id;
                    }
                }
                // PERF2 auditoría: Facultades/Programas/Espacios/Asignaturas/Grupos se trackearon
                // (_uow.Track) sin guardar todavía — un solo flush aquí en vez de uno por fila. Si
                // el Excel no trae sesiones predefinidas, este es el ÚNICO SaveAsync antes del
                // commit; sin él, todo lo de arriba se habría descartado en silencio.
                await _uow.SaveAsync();

                // P0-2/P0-5 auditoría: las filas día/hora del Excel ya no se persisten como Sesiones.
                // Ningún horario las contenía, así que ninguna pantalla las mostraba, pero bloqueaban
                // docentes y guardaban el EspacioId temporal del lector (409 con la FK de M14, que
                // revertía el import completo). Solo alimentan el requisito de aula de cada grupo
                // (espacioPorAsignatura, arriba).
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            // Post-transacción: estadísticas adicionales.
            // El docente vive en el grupo: "sin docente" = grupos sin docente asignado.
            stats.GruposSinDocente = resultado.Grupos.Count(g => !g.DocenteId.HasValue);
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
