using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Orquesta el pipeline de 3 fases para generar un horario académico.
    /// Fase 1 — GraphColoring (pre-asignación de bloques sin conflictos).
    /// Fase 2 — CP-SAT ConstraintProgramming (factibilidad con restricciones duras).
    /// Fase 3 — Genetic Algorithm (optimización de restricciones blandas).
    /// </summary>
    public class GenerarHorarioService
    {
        private readonly IMotorColoracionGrafo       _fase1;
        private readonly IMotorConstraintProgramming  _fase2;
        private readonly IMotorGenetico              _fase3;
        private readonly IHorarioRepositorio         _horarioRepo;
        private readonly ISesionRepositorio          _sesionRepo;
        private readonly IAsignacionSemanalRepositorio _asignacionRepo;
        private readonly IGrupoRepositorio           _grupoRepo;
        private readonly ICriterioCesionAlternanciaRepositorio _criterioCesionRepo;
        private readonly IUnitOfWork                 _uow;

        public GenerarHorarioService(
            IMotorColoracionGrafo       fase1,
            IMotorConstraintProgramming  fase2,
            IMotorGenetico              fase3,
            IHorarioRepositorio         horarioRepo,
            ISesionRepositorio          sesionRepo,
            IAsignacionSemanalRepositorio asignacionRepo,
            IGrupoRepositorio           grupoRepo,
            ICriterioCesionAlternanciaRepositorio criterioCesionRepo,
            IUnitOfWork                 uow)
        {
            _fase1       = fase1;
            _fase2       = fase2;
            _fase3       = fase3;
            _horarioRepo = horarioRepo;
            _sesionRepo  = sesionRepo;
            _asignacionRepo = asignacionRepo;
            _grupoRepo   = grupoRepo;
            _criterioCesionRepo = criterioCesionRepo;
            _uow         = uow;
        }

        /// <summary>
        /// Recupera el horario vigente ya persistido para un semestre (última corrida generada),
        /// reconstruyendo el mismo DTO que devuelve POST /generar. Antes no existía ningún GET —
        /// el horario generado solo vivía en memoria del navegador y un simple reload de la página
        /// lo perdía por completo aunque siguiera intacto en BD (P6 auditoría).
        /// </summary>
        public async Task<GenerarHorarioResponse?> ObtenerActualAsync(string semestre)
        {
            var horario = await _horarioRepo.GetBySemestreAsync(semestre);
            if (horario == null) return null;

            var sesionIds = horario.SesioneIds.ToHashSet();
            var sesiones = (await _sesionRepo.GetAllAsync())
                .Where(s => sesionIds.Contains(s.Id))
                .ToList();
            if (sesiones.Count == 0) return null;

            var asignaciones = await _asignacionRepo.GetBySesionIdsAsync(sesionIds);
            var grupos = await _grupoRepo.GetAllAsync();
            var bloques = GenerarBloquesTiempo();

            return new GenerarHorarioResponse
            {
                HorarioId      = horario.Id,
                Semestre       = horario.Semestre,
                EsFactible     = true,
                PuntajeFitness = horario.PuntajeFitness,
                Sesiones       = ConstruirSesionesDto(sesiones, asignaciones, grupos, bloques)
            };
        }

        public async Task<GenerarHorarioResponse> EjecutarAsync(GenerarHorarioRequest request, CancellationToken ct = default)
        {
            var logs = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            logs.Add($"[INFO] Iniciando pipeline de optimización con {request.Asignaturas.Count} asignaturas, {request.Docentes.Count} docentes y {request.Espacios.Count} espacios.");

            // ── 1. Convertir DTOs del frontend a entidades de dominio ───────────
            var (espacios, advertenciasEspacios) = MapearEspacios(request.Espacios);
            logs.AddRange(advertenciasEspacios);
            var bloques   = GenerarBloquesTiempo();
            var docentes  = MapearDocentes(request.Docentes, bloques);
            // Multi-grupo real: cada grupo lleva su propio Id como GrupoId de sus sesiones, así que
            // HC-C01 (solape) y HC-G01 (franja) se aplican por grupo, no sobre un id sintético
            // compartido por todo el run.
            var (grupos, advertenciasGrupos) = MapearGrupos(request.Grupos);
            logs.AddRange(advertenciasGrupos);

            // Auditoría de entrada (detector de truncamiento en el contrato Angular↔API): sin esto,
            // "el usuario no configuró requisito" y "el requisito se perdió en el mapeo del frontend"
            // son indistinguibles — ambos terminan en RequisitosEspacio vacío y 0 violaciones.
            int gruposConRequisito = grupos.Count(g => g.RequisitosEspacio.Count > 0);
            int gruposConDisponibilidad = grupos.Count(g => !string.IsNullOrWhiteSpace(g.DisponibilidadUiJson));
            logs.Add($"[INFO] Grupos: {grupos.Count} · con requisito de espacio: {gruposConRequisito} · con disponibilidad: {gruposConDisponibilidad}.");
            if (grupos.Count > 0 && gruposConRequisito == 0)
                logs.Add("[WARN] Ningún grupo trae requisito de espacio: se aplicará la regla por defecto por tipo de sesión a todas las sesiones presenciales.");

            // SC-PRES: mapa de categoría por asignatura (alimenta el criterio "Electiva" de la lista
            // de cesión) y de elegibilidad explícita (criterio "Elegible", marcado por el departamento).
            var categoriaPorAsig = request.Asignaturas
                .ToDictionary(
                    dto => Guid.TryParse(dto.Id, out var aid) ? aid : Guid.Empty,
                    dto => ParseCategoria(dto.Categoria));
            var elegiblePorAsig = request.Asignaturas
                .ToDictionary(
                    dto => Guid.TryParse(dto.Id, out var aid) ? aid : Guid.Empty,
                    dto => dto.EsCandidataAlternancia);

            // HC-VH: ventana horaria por asignatura (la fija Secretaría Académica). Se pasa a CP-SAT
            // como hard constraint — ninguna sesión se asigna fuera de [HoraInicioMin, HoraFinMax].
            var ventanaPorAsig = request.Asignaturas
                .ToDictionary(
                    dto => Guid.TryParse(dto.Id, out var aid) ? aid : Guid.Empty,
                    dto => (ParseHora(dto.HoraInicioMin), ParseHora(dto.HoraFinMax)));

            var (sesiones, advertenciasSesiones) = MapearSesionesIniciales(grupos, request.Asignaturas);
            logs.AddRange(advertenciasSesiones);
            logs.Add($"[INFO] Sesiones creadas a partir de grupos: {sesiones.Count}.");

            // ── 1b. Sesiones fijas (horario base) — se añaden con bloque pre-asignado ──
            // Sin grupo propio (SesionFijaDto no lo trae): comparten un id sintético entre sí,
            // suficiente para la exclusión mutua de horario que necesitan (no participan de HC-G01).
            var sesionesFijasGrupoId = Guid.NewGuid();
            var sesionesFijasIds = new HashSet<Guid>();
            int sesionesFijasOmitidas = 0;
            if (request.SesionesFijas is { Count: > 0 })
            {
                var (fijas, omitidas) = MapearSesionesFijas(request.SesionesFijas, bloques, sesionesFijasGrupoId);
                sesionesFijasIds = fijas.Select(s => s.Id).ToHashSet();
                sesionesFijasOmitidas = omitidas.Count;
                sesiones.AddRange(fijas);
                logs.Add($"[INFO] {fijas.Count} sesión(es) fijas del horario base añadidas.");
                foreach (var motivo in omitidas)
                    logs.Add($"[WARN] Sesión fija omitida: {motivo}");
            }

            // Lista ordenada/activable de criterios de cesión (MultiplesSesiones / Electiva / Optativa /
            // Elegible). Consultada por Etapa 1 (teoría, heurística) y Etapa 2 (labs, reactiva) — un
            // mismo predicado por criterio activo, en el orden configurado. Una sesión sin ningún
            // criterio activo que la alcance nunca es candidata a cesión (no hay regla implícita por
            // categoría). "MultiplesSesiones" es la excepción: no otorga elegibilidad por sí solo, solo
            // desempata el orden entre sesiones ya elegibles por otro criterio (ver AplicarPrioridadPresencial).
            var criteriosActivos = (await _criterioCesionRepo.GetAllAsync())
                .Where(c => c.Activo)
                .OrderBy(c => c.Orden)
                .ToList();
            var totalSesionesPorAsig = sesiones
                .GroupBy(s => s.AsignaturaId)
                .ToDictionary(g => g.Key, g => g.Count());
            var predicadosCesion = criteriosActivos
                .Select(c => (c.Criterio, Predicado: (Func<Sesion, bool>)(s => c.Criterio switch
                {
                    CriterioElegibilidadAlternancia.Electiva =>
                        categoriaPorAsig.TryGetValue(s.AsignaturaId, out var catE) && catE == CategoriaAsignatura.Electiva,
                    CriterioElegibilidadAlternancia.Optativa =>
                        categoriaPorAsig.TryGetValue(s.AsignaturaId, out var catO) && catO == CategoriaAsignatura.Optativa,
                    CriterioElegibilidadAlternancia.Elegible =>
                        elegiblePorAsig.TryGetValue(s.AsignaturaId, out var eleg) && eleg,
                    CriterioElegibilidadAlternancia.MultiplesSesiones =>
                        totalSesionesPorAsig.TryGetValue(s.AsignaturaId, out var n) && n >= 2,
                    _ => false
                })))
                .ToList();

            // ── 1c. Contexto de emparejamiento (Semana B) ──────────────────────────────
            // No se cede NADA todavía: la Semana B solo se activa de forma reactiva, cuando CP-SAT
            // demuestra que no existe configuración válida por falta de aulas. El contexto se
            // construye una vez y se reutiliza en cada intento del bucle.
            var contextoCesion = ConstruirContextoCesion(sesiones, espacios, grupos, bloques, ventanaPorAsig);
            var sesionesCedidasEnOrden = new List<Guid>();
            var parejasDescartadas = new HashSet<(Guid, Guid)>();
            var diagnosticoCesion = new List<string>();

            // ── 2. Fase 1 — Coloración de Grafo (pre-asignación de bloques) ────
            // HC-G01/HC-VH: se pasan grupos y ventanas para que el warm-start caiga siempre dentro
            // del dominio que CP-SAT va a exigir (misma fuente: CalculadorDominioSesion).
            logs.Add("[INFO] Fase 1: Pre-procesamiento (Coloración de grafos) iniciada.");
            var swFase1 = System.Diagnostics.Stopwatch.StartNew();
            var sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(
                sesiones, bloques, grupos, ventanaPorAsig, ct)).ToList();
            swFase1.Stop();
            logs.Add($"[INFO] Fase 1 completada en {swFase1.ElapsedMilliseconds}ms.");

            // ── 3. Fase 2 — Constraint Programming (factibilidad) ─────────────
            // HC-G01: se pasan los grupos para que CP-SAT aplique la disponibilidad horaria
            // como restricción dura (presencial-first: el pipeline se optimiza alrededor del
            // horario de los grupos, no de los docentes).
            logs.Add("[INFO] Fase 2: Viabilidad (CP-SAT) iniciada.");
            var swFase2 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                sesionesColoreadas, bloques, espacios,
                grupos: grupos,
                sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
                ventanaPorAsignatura: ventanaPorAsig,
                ct);

            // ── Activación de la Semana B (reactiva) ────────────────────────────────────
            // Si Fase 2 dice que no hay configuración válida POR FALTA DE AULAS, se cede una pareja
            // a alternancia y se reintenta. Cada pareja libera su aula una semana de cada dos, que
            // es lo único que crea capacidad nueva. Si la infactibilidad es por otra causa (ventana
            // horaria, franja de grupo, datos, timeout), ceder no ayuda y no se entra al bucle.
            int intentosCesion = 0;
            int maxIntentosCesion = sesiones.Count; // cada intento cuesta un solve completo
            while (!resultadoFactibilidad.EsFactible
                   && resultadoFactibilidad.Motivo == MotivoInfactibilidad.Espacio
                   && intentosCesion++ < maxIntentosCesion)
            {
                var cesion = CederSiguientePareja(
                    sesiones, predicadosCesion, sesionesFijasIds, contextoCesion, parejasDescartadas);
                if (!cesion.Cedio)
                {
                    diagnosticoCesion = cesion.Diagnostico.ToList();
                    break;
                }

                var (s1, s2) = (cesion.S1!, cesion.S2!);
                var previo = resultadoFactibilidad;

                sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(
                    sesiones, bloques, grupos, ventanaPorAsig, ct)).ToList();
                resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                    sesionesColoreadas, bloques, espacios,
                    grupos: grupos,
                    sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
                    ventanaPorAsignatura: ventanaPorAsig,
                    ct);

                if (!resultadoFactibilidad.EsFactible &&
                    resultadoFactibilidad.Motivo is not (MotivoInfactibilidad.Espacio or MotivoInfactibilidad.Timeout))
                {
                    // La pareja introdujo una contradicción (p. ej. HC-SEP con las hermanas de sus
                    // miembros). Deshacerla y descartarla, en vez de abandonar el bucle: la
                    // siguiente combinación puede funcionar.
                    s1.RevertirCesion();
                    s2.RevertirCesion();
                    parejasDescartadas.Add(ClaveDePareja(s1.Id, s2.Id));
                    resultadoFactibilidad = previo;
                    logs.Add($"[WARN] La pareja de alternancia propuesta volvió el horario infactible " +
                             $"({previo.Motivo} → {MotivoInfactibilidad.Otro}); se descarta y se prueba otra combinación.");
                    continue;
                }

                sesionesCedidasEnOrden.Add(s1.Id);
                sesionesCedidasEnOrden.Add(s2.Id);
                logs.Add($"[INFO] Semana B: se emparejaron 2 sesiones para alternar aula " +
                         $"({sesionesCedidasEnOrden.Count / 2} pareja(s) en total). " +
                         $"Criterios activos en orden: {string.Join(" → ", criteriosActivos.Select(c => c.Criterio))}.");

                if (resultadoFactibilidad.Motivo == MotivoInfactibilidad.Timeout) break;
            }
            swFase2.Stop();

            if (!resultadoFactibilidad.EsFactible)
            {
                logs.Add($"[ERROR] Fase 2 falló en {swFase2.ElapsedMilliseconds}ms [{resultadoFactibilidad.Motivo}]: {resultadoFactibilidad.MensajeError}");

                // Nada se virtualiza en silencio: si el horario no cabe y la alternancia no puede
                // absorberlo, se falla nombrando qué quedó fuera y por qué no encontró pareja.
                var mensaje = resultadoFactibilidad.MensajeError;
                if (diagnosticoCesion.Count > 0)
                {
                    foreach (var motivo in diagnosticoCesion) logs.Add($"[ERROR] Sin pareja de alternancia: {motivo}");
                    mensaje += " No se pudo activar la Semana B para absorber lo que no cabe: " +
                               string.Join(" ", diagnosticoCesion.Take(3));
                }

                return new GenerarHorarioResponse
                {
                    HorarioId     = Guid.NewGuid(),
                    Semestre      = request.Semestre,
                    EsFactible    = false,
                    MensajeError  = mensaje,
                    MotivoInfactibilidad = resultadoFactibilidad.Motivo.ToString(),
                    GruposEnConflicto = resultadoFactibilidad.GruposResponsablesIds?.Select(id => id.ToString()).ToList() ?? new(),
                    SesionesSinAlternanciaPosible = diagnosticoCesion,
                    Logs          = logs,
                    Sesiones      = new List<SesionGeneradaDto>()
                };
            }
            logs.Add($"[INFO] Fase 2 completada exitosamente en {swFase2.ElapsedMilliseconds}ms" +
                     (sesionesCedidasEnOrden.Count > 0
                        ? $" con {sesionesCedidasEnOrden.Count / 2} pareja(s) alternando en la Semana B."
                        : " sin necesidad de alternancia: todo cabe en la Semana A."));

            // ── 4. Fase 3 — Algoritmo Genético (optimización de restricciones blandas) ──
            // Optimiza objetivos del docente (huecos, > 6 horas seguidas, balance entre días
            // disponibles) moviendo el inicio de cada sesión, compartido por las semanas A/B.
            // Preserva todas las restricciones duras de la Fase 2; ante cualquier duda hace
            // fallback interno a la solución de Fase 2.
            logs.Add("[INFO] Fase 3: Optimización (Algoritmo Genético) iniciada.");
            // SC-PRES: info por asignatura (sesiones/semana reales + categoría) para que el fitness
            // penalice proporcionalmente las sesiones que cedieron presencialidad (ver EvaluadorFitness).
            var infoAsignatura = sesionesColoreadas
                .GroupBy(s => s.AsignaturaId)
                .ToDictionary(
                    g => g.Key,
                    g => (g.Count(),
                          categoriaPorAsig.TryGetValue(g.Key, out var cat) ? cat : CategoriaAsignatura.Obligatoria));

            var swFase3 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoGA = await _fase3.OptimizarAsync(
                sesionesColoreadas, resultadoFactibilidad.Asignaciones, bloques, espacios, docentes,
                grupos: grupos,
                config: MapearConfiguracion(request.Configuracion),
                infoAsignatura: infoAsignatura,
                ventanaPorAsignatura: ventanaPorAsig,   // HC-VH: el GA no puede sacar sesiones de su ventana
                sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null, // regla 8: el GA no mueve el horario base
                sesionesCedidasParaRevertir: sesionesCedidasEnOrden.Count > 0 ? sesionesCedidasEnOrden : null,
                ct: ct);
            swFase3.Stop();
            logs.Add($"[INFO] Fase 3 completada en {swFase3.ElapsedMilliseconds}ms. Fitness={resultadoGA.PuntajeFitness}, " +
                     $"generaciones={resultadoGA.Generaciones}, fallback={resultadoGA.UsoFallback}.");
            if (resultadoGA.SesionesRevertidasIds is { Count: > 0 })
                logs.Add($"[INFO] Pase de reversión: {resultadoGA.SesionesRevertidasIds.Count} sesión(es) recuperadas a " +
                         "presencial tras validar el empaque real de aulas del GA.");

            var asignaciones   = resultadoGA.AsignacionesOptimizadas;
            var puntajeFitness = resultadoGA.PuntajeFitness;

            // ── 4b. Post-chequeo de restricciones duras (P0.3 + P1.8 auditoría) ─────────
            // Verificamos las asignaciones FINALES contra las 10 reglas duras que CP-SAT impuso
            // en Fase 2: HC-C01/HC-S01 (solapes) + HC-VH/HC-G01/HC-CAP/HC-S03/HC-S05 (contexto) +
            // HC-ALT/HC-SEP/HC-BASE (alternancia y horario base — Fase 1/Fase 3 del plan de saneamiento).
            // Si el GA introdujo cualquier violación, fallback a las asignaciones de Fase 2.
            var sesionPorIdValidacion = sesionesColoreadas.ToDictionary(s => s.Id);
            var bloqueIndex = Enumerable.Range(0, bloques.Count).ToDictionary(i => bloques[i].Id, i => i);
            var contextoValidacion = new ContextoValidacion(
                Bloques: bloques,
                VentanaPorAsignatura: ventanaPorAsig,   // los nombres de tupla no afectan la conversión
                DisponibilidadPorGrupo: grupos
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().ObtenerDisponibilidadSemanal()),
                EstudiantesPorGrupo: grupos
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().EstudiantesInscritos),
                EspacioPorId: espacios.ToDictionary(e => e.Id),
                SesionesFijas: sesionesFijasIds,
                RequisitosPorGrupo: grupos
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().RequisitosEspacio),
                // G2/G7 auditoría: sin esto, los mensajes de conflicto degradan a "sin nombre" —
                // la fuente ya existe (AsignaturaDto.Nombre / Grupo.Nombre), es gratis pasarla.
                NombrePorAsignatura: request.Asignaturas
                    .Where(dto => Guid.TryParse(dto.Id, out _))
                    .ToDictionary(dto => Guid.Parse(dto.Id), dto => dto.Nombre),
                NombrePorGrupo: grupos
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().Nombre));

            var conflictos = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex, contextoValidacion);
            if (conflictos.Count > 0)
            {
                // Fase 3 (M4, instrumentación): antes de M4 esta rama se disparaba en casi
                // cualquier corrida con parejas de alternancia (HC-ALT) o ≥2 sesiones semanales
                // del mismo tipo (HC-SEP) — MotorGenetico reportaba UsoFallback=false (sus propios
                // chequeos internos, HC-C01 + aulas, pasaban) pero el post-chequeo aquí sí las
                // detectaba. La razón exacta (regla + detalle) queda en el log en vez de solo el
                // conteo, para distinguir esa causa de un fallback interno de MotorGenetico.
                logs.Add($"[WARN] Post-chequeo detectó {conflictos.Count} violación(es) en la salida del GA " +
                         $"(MotorGenetico.UsoFallback={resultadoGA.UsoFallback}); usando la solución de Fase 2.");
                foreach (var c in conflictos.Take(20)) logs.Add($"[WARN] {c}");
                asignaciones   = resultadoFactibilidad.Asignaciones;
                puntajeFitness = 0m;
                conflictos     = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex, contextoValidacion);
            }

            if (conflictos.Count > 0)
            {
                // Ni siquiera la solución de Fase 2 valida (no debería ocurrir): no publicar.
                logs.Add($"[ERROR] La solución de Fase 2 viola {conflictos.Count} restricción(es) dura(s).");
                foreach (var c in conflictos.Take(20)) logs.Add($"[ERROR] {c}");
                return new GenerarHorarioResponse
                {
                    HorarioId    = Guid.NewGuid(),
                    Semestre     = request.Semestre,
                    EsFactible   = false,
                    MensajeError = $"El horario generado viola {conflictos.Count} restricción(es) dura(s) y no puede publicarse. " +
                                   string.Join(" ", conflictos.Take(5)),
                    Logs         = logs,
                    Sesiones     = new List<SesionGeneradaDto>()
                };
            }
            logs.Add("[INFO] Post-chequeo OK: 0 violaciones de restricciones duras.");

            // ── 5. Persistir sesiones lógicas, Horario y asignaciones semanales ─
            // Las tres escrituras comparten el mismo DbContext (scoped), así que van en UNA
            // transacción: si la última falla, no quedan sesiones ni horario huérfanos sin
            // asignaciones. Antes eran tres SaveChanges independientes (auditoría dotnet).
            var horario = new Domain.Entities.Horario(
                id: Guid.NewGuid(),
                semestre: request.Semestre,
                sesionIds: sesionesColoreadas.Select(s => s.Id).ToList(),
                violacionesRestriccionesDuras: conflictos.Count,
                puntajeFitness: puntajeFitness);

            await _uow.BeginTransactionAsync();
            try
            {
                // M2 auditoría: cada corrida ANTES solo agregaba (AddRangeAsync sin delete previo),
                // dejando las sesiones/asignaciones de corridas superadas huérfanas en la BD —
                // crecimiento ilimitado y cada consumidor (AsignarDocenteSesionService,
                // ReacomodarHorarioService) tuvo que aprender a filtrarlas por su cuenta (ver
                // comentario "G4 auditoría" en AsignarDocenteSesionService). El registro Horario en
                // sí NO se borra (auditoría de corridas — IHorarioRepositorio.GetAllAsync), pero sus
                // sesiones y asignaciones son datos regenerables (regla 8, CLAUDE.md) y sí se limpian
                // antes de escribir la corrida nueva. Las sesiones manuales (CrearSesionManualService)
                // nunca pertenecen a un Horario.SesioneIds, así que sobreviven intactas.
                // Limpieza de sesión de limpieza: esto ANTES no filtraba por semestre — regenerar
                // "2026-2" borraba también las sesiones vivas de "2026-1", dejando su Horario con
                // SesioneIds colgando (ObtenerActualAsync empezaba a devolver 404 para ese semestre
                // aunque nadie lo hubiera tocado). Solo las corridas DEL MISMO semestre quedan
                // superadas por esta.
                var horariosAnteriores = (await _horarioRepo.GetAllAsync())
                    .Where(h => h.Semestre == request.Semestre)
                    .ToList();
                var sesionIdsAnteriores = horariosAnteriores.SelectMany(h => h.SesioneIds).ToHashSet();
                if (sesionIdsAnteriores.Count > 0)
                {
                    var asignacionesAnteriores = await _asignacionRepo.GetBySesionIdsAsync(sesionIdsAnteriores);
                    foreach (var a in asignacionesAnteriores)
                        await _asignacionRepo.DeleteAsync(a.Id);
                    foreach (var sesionId in sesionIdsAnteriores)
                        await _sesionRepo.DeleteAsync(sesionId);
                    logs.Add($"[INFO] Limpiadas {sesionIdsAnteriores.Count} sesión(es) de corridas anteriores antes de persistir la nueva.");
                }

                await _sesionRepo.AddRangeAsync(sesionesColoreadas);
                await _horarioRepo.AddAsync(horario);
                await _asignacionRepo.AddRangeAsync(asignaciones);
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            stopwatch.Stop();
            logs.Add($"[INFO] Pipeline total ejecutado en {stopwatch.ElapsedMilliseconds}ms.");

            // ── 6. Mapear asignaciones al DTO de respuesta (una DTO por semana) ─
            var sesionesDto = ConstruirSesionesDto(sesionesColoreadas, asignaciones, grupos, bloques);

            return new GenerarHorarioResponse
            {
                HorarioId      = horario.Id,
                Semestre       = request.Semestre,
                EsFactible     = true,
                PuntajeFitness = puntajeFitness,
                Generaciones   = resultadoGA.Generaciones,
                PenalizacionPresencial = resultadoGA.PenalizacionPresencial,
                SesionesFijasOmitidas = sesionesFijasOmitidas,
                Logs           = logs,
                Sesiones       = sesionesDto
            };
        }

        /// <summary>
        /// Post-chequeo de restricciones duras sobre las asignaciones finales: las 10 reglas que
        /// CP-SAT impone en Fase 2 — HC-C01, HC-S01, HC-ALT, HC-SEP y (vía contexto) HC-VH, HC-G01,
        /// HC-CAP, HC-S03, HC-S05, HC-BASE. El docente está fuera del pipeline (CR-08), así que no
        /// se valida carga semanal de docente (HC-I03).
        /// </summary>
        private static IReadOnlyList<string> Validar(
            IReadOnlyList<AsignacionSemanal> asignaciones,
            IReadOnlyDictionary<Guid, Sesion> sesionPorId,
            IReadOnlyDictionary<Guid, int> bloqueIndex,
            ContextoValidacion contexto)
        {
            return ValidadorRestriccionesDuras.Validar(asignaciones, sesionPorId, bloqueIndex, contexto);
        }

        // ── Helpers de mapeo ─────────────────────────────────────────────────────

        /// <summary>
        /// Mapea las sesiones del horario base a entidades de dominio con el BloqueTiempo ya
        /// pre-asignado (buscando el bloque por día+horaInicio en la grilla canónica).
        /// Si no se encuentra un bloque coincidente, la sesión se omite y el motivo se reporta
        /// en <c>omitidas</c> (antes se descartaba en silencio).
        /// </summary>
        private static (List<Sesion> fijas, List<string> omitidas) MapearSesionesFijas(
            List<SesionFijaDto> dtos,
            List<BloqueTiempo> bloques,
            Guid sesionesFijasGrupoId)
        {
            // Índice rápido: (dia, horaInicio) → BloqueTiempo
            var bloqueDict = bloques.ToDictionary(
                b => (DiaToString(b.Dia), b.HoraInicio.ToString("HH:mm")));

            var resultado = new List<Sesion>();
            var omitidas  = new List<string>();
            foreach (var dto in dtos)
            {
                if (!bloqueDict.TryGetValue((dto.Dia.ToLowerInvariant(), dto.HoraInicio), out var bloque))
                {
                    omitidas.Add($"asignatura {dto.AsignaturaId}, {dto.Dia} {dto.HoraInicio} no coincide con ningún bloque de la grilla canónica.");
                    continue;
                }

                // Bug: un AsignaturaId que no parsea caía a Guid.NewGuid() — una sesión fantasma
                // (sin categoría, sin ventana, sin nombre en los mensajes de conflicto) en vez de
                // reportarse en omitidas, que es justo el contrato que este método documenta.
                if (!Guid.TryParse(dto.AsignaturaId, out var asigId))
                {
                    omitidas.Add($"AsignaturaId '{dto.AsignaturaId}' no es un identificador válido ({dto.Dia} {dto.HoraInicio}).");
                    continue;
                }
                Guid? espId = Guid.TryParse(dto.EspacioId, out var eid) ? eid : null;
                // Bug: docenteId: null se ignoraba el docente aunque el frontend sí lo manda —
                // regenerar con horario base borraba el docente de todas sus sesiones fijas.
                Guid? docId = Guid.TryParse(dto.DocenteId, out var did) ? did : null;

                var alternancia = dto.Alternancia?.Trim().ToLowerInvariant() switch
                {
                    "tipoa"          => TipoAlternancia.TipoA,
                    "tipob"          => TipoAlternancia.TipoB,
                    "sinalternancia" => TipoAlternancia.SinAlternancia,
                    _                => TipoAlternancia.SinAlternancia
                };

                var id = Guid.TryParse(dto.Id, out var sid) ? sid : Guid.NewGuid();
                var sesion = new Sesion(
                    id: id,
                    asignaturaId: asigId,
                    docenteId: docId,
                    bloqueId: bloque.Id,
                    espacioId: espId,
                    grupoId: sesionesFijasGrupoId,
                    alternancia: alternancia,
                    modalidad: dto.Virtual ? Modalidad.Virtual : Modalidad.Presencial,
                    duracionHoras: dto.DuracionHoras > 0 ? dto.DuracionHoras : 2m,
                    esBloque: false,
                    estaDividida: false,
                    tipoFlujo: ParseTipoFlujo(dto.TipoFlujo));

                // Marca la sesión como ya asignada para que la Fase 1 (ColoracionGrafo)
                // no la reasigne. CP-SAT recibirá su ID en sesionesFijasIds y le añadirá
                // una restricción de igualdad (no solo un hint).
                sesion.AsignarBloqueTiempo(bloque.Id);
                if (espId.HasValue) sesion.AsignarEspacio(espId.Value);

                resultado.Add(sesion);
            }
            return (resultado, omitidas);
        }

        // M8: literales reconocidos para el tipo de un ESPACIO real — coincide con el contrato de
        // EspaciosController (que envía "Salón" con tilde, distinto del "Salon" sin tilde que usa
        // RequisitoEspacioDto.TipoEspacio, ver ParseTipoEspacioRequisito). Cualquier otro valor cae
        // al default conservador (Salon) de ParseTipoEspacio, pero ahora se advierte en los logs en
        // vez de mezclarse en silencio con los salones legítimos.
        private static readonly HashSet<string> LiteralesTipoEspacioReconocidos =
            new(StringComparer.OrdinalIgnoreCase) { "laboratorio", "auditorio", "salon", "salón" };

        private static (List<Espacio> espacios, List<string> advertencias) MapearEspacios(List<EspacioDto> dtos)
        {
            var advertencias = new List<string>();
            var espacios = dtos.Select(dto =>
            {
                if (!string.IsNullOrWhiteSpace(dto.Tipo) && !LiteralesTipoEspacioReconocidos.Contains(dto.Tipo))
                    advertencias.Add($"[WARN] Espacio '{dto.Nombre}': tipo '{dto.Tipo}' no reconocido, se usó Salón por defecto.");
                return new Espacio(
                    id: Guid.TryParse(dto.Id, out var eid) ? eid : Guid.NewGuid(),
                    nombre: dto.Nombre,
                    tipo: ParseTipoEspacio(dto.Tipo),
                    capacidad: dto.Capacidad > 0 ? dto.Capacidad : 30);
            }).ToList();
            return (espacios, advertencias);
        }

        private static List<Docente> MapearDocentes(List<DocenteDto> dtos, List<BloqueTiempo> bloques)
        {
            return dtos.Select(dto =>
            {
                var id = Guid.TryParse(dto.Id, out var did) ? did : Guid.NewGuid();
                var maxHoras = dto.MaxHoras.HasValue && dto.MaxHoras > 0 ? dto.MaxHoras.Value : 20m;

                // Fuente única del parseo por día (A1): sin información → sin restricción (todos los
                // bloques); día explícitamente no-disponible → cerrado; el resto respeta su ventana.
                // M1: "disponibilidad": null explícito en el JSON pisa el default del DTO (los
                // property initializers de System.Text.Json no protegen contra null explícito).
                var disponibilidad = DisponibilidadSemanal.Desde(MapearEntradasCrudas(dto.Disponibilidad ?? new()));
                var bloquesDisponibles = bloques
                    .Where(b => disponibilidad.PermiteBloque(b.Dia, b.HoraInicio, b.HoraFin))
                    .ToList();

                // Docente exige una lista no vacía (validación de dominio); sin restricción real
                // equivale a "ambas franjas".
                var disponibilidadFinal = disponibilidad.ComoFranjasCoarse();
                if (disponibilidadFinal.Count == 0)
                    disponibilidadFinal = new List<FranjaHoraria> { FranjaHoraria.Matutino, FranjaHoraria.Vespertino };

                var docente = new Docente(
                    id: id,
                    nombre: dto.Nombre,
                    apellido: string.Empty,
                    correo: $"docente-{id}@soea.edu",
                    maximoHorasSemanales: maxHoras,
                    disponibilidad: disponibilidadFinal);

                foreach (var bloque in bloquesDisponibles)
                    docente.AgregarBloqueDisponibilidad(bloque);

                return docente;
            }).ToList();
        }

        private static Dictionary<string, DisponibilidadSemanal.DiaEntradaCruda> MapearEntradasCrudas(
            Dictionary<string, DisponibilidadDiaDto> disponibilidad) =>
            disponibilidad.ToDictionary(
                kv => kv.Key,
                kv => new DisponibilidadSemanal.DiaEntradaCruda(
                    kv.Value.NoDisponible, kv.Value.Tipo, kv.Value.FranjaGeneral, kv.Value.Desde, kv.Value.Hasta));

        /// <summary>
        /// Multi-grupo real (P1): itera grupos, no asignaturas — cada grupo expande los 3 tracks
        /// de SU asignatura con SU GrupoId. Antes todas las sesiones del run compartían un
        /// GrupoId sintético (<c>grupoIdRun</c>), lo que serializaba el run entero contra HC-C01.
        /// internal (no private) para verificación directa del mapeo en SOEA.Tests. Un grupo cuya
        /// asignatura no resuelve se descartaba en silencio (G3 auditoría) — ahora queda una
        /// advertencia nombrada.
        /// </summary>
        internal static (List<Sesion> sesiones, List<string> advertencias) MapearSesionesIniciales(
            List<Grupo> grupos,
            List<AsignaturaDto> asignaturasDtos)
        {
            var sesiones = new List<Sesion>();
            var advertencias = new List<string>();
            // Bloque placeholder — Fase 1 lo reemplazará
            var bloqueTemp = Guid.NewGuid();
            var asignaturaPorId = asignaturasDtos
                .Where(dto => Guid.TryParse(dto.Id, out _))
                .ToDictionary(dto => Guid.Parse(dto.Id));

            foreach (var grupo in grupos)
            {
                if (grupo.AsignaturaId is not { } asigId || !asignaturaPorId.TryGetValue(asigId, out var dto))
                {
                    advertencias.Add($"[WARN] Grupo '{grupo.Nombre}' (Id {grupo.Id}) no tiene una asignatura válida y no se incluyó en el horario.");
                    continue;
                }

                // CR-02/CR-08 (presencial-first): el docente del grupo es solo SEMILLA de la
                // sesión generada, no restricción — HC-I01/HC-I02/HC-I03 siguen fuera del
                // pipeline. El eje de conflicto y de optimización sigue siendo la cohorte
                // (GrupoId). PATCH /api/sesiones/{id}/docente puede sobrescribirla después.

                // Fix #5: case-insensitive alternancia matching. Solo aplica al track de
                // laboratorio — teoría (presencial o virtual) siempre es SinAlternancia.
                var alternanciaLab = dto.Alternancia?.Trim().ToLowerInvariant() switch
                {
                    "tipoa"          => TipoAlternancia.TipoA,
                    "tipob"          => TipoAlternancia.TipoB,
                    "sinalternancia" => TipoAlternancia.SinAlternancia,
                    _                => TipoAlternancia.SinAlternancia
                };

                // HC-S05: espacio concreto exigido por el grupo para este tipo de sesión, si lo hay
                // (reemplaza a Asignatura.EspacioFijoId — ver Grupo.RequisitosEspacio).
                Guid? EspacioFijoDe(TipoSesion tipo) =>
                    grupo.RequisitosEspacio.FirstOrDefault(r => r.TipoSesion == tipo)?.EspacioId;

                void Agregar(int cantidad, int duracionHoras, TipoFlujo tipoFlujo, Modalidad modalidad,
                    TipoAlternancia alternancia, TipoSesion tipoSesion)
                {
                    // Teoría virtual nunca tiene espacio (regla 9 CLAUDE.md): es sincrónica online.
                    Guid? espacioFijo = modalidad == Modalidad.Virtual ? null : EspacioFijoDe(tipoSesion);
                    for (int i = 0; i < cantidad; i++)
                    {
                        sesiones.Add(new Sesion(
                            id: Guid.NewGuid(),
                            asignaturaId: asigId,
                            docenteId: grupo.DocenteId,
                            bloqueId: bloqueTemp,
                            espacioId: espacioFijo,
                            grupoId: grupo.Id,
                            alternancia: alternancia,
                            modalidad: modalidad,
                            duracionHoras: duracionHoras,
                            esBloque: false,
                            estaDividida: false,
                            tipoFlujo: tipoFlujo));
                    }
                }

                Agregar(dto.SesionesTeoriaPresencialSemana, dto.HorasTeoriaPresencial,
                    TipoFlujo.AulaVirtual, Modalidad.Presencial, TipoAlternancia.SinAlternancia, TipoSesion.TeoriaPresencial);
                Agregar(dto.SesionesTeoriaVirtualSemana, dto.HorasTeoriaVirtual,
                    TipoFlujo.AulaVirtual, Modalidad.Virtual, TipoAlternancia.SinAlternancia, TipoSesion.TeoriaVirtual);
                Agregar(dto.SesionesLaboratorioSemana, dto.HorasLaboratorio,
                    TipoFlujo.Laboratorio, Modalidad.Presencial, alternanciaLab, TipoSesion.Laboratorio);
            }
            return (sesiones, advertencias);
        }

        /// <summary>
        /// Genera la grilla canónica de bloques de tiempo institucional.
        /// C1 auditoría: la fuente única del rango horario es <see cref="GrillaInstitucional"/>.
        /// </summary>
        private static List<BloqueTiempo> GenerarBloquesTiempo() => GrillaInstitucional.GenerarBloques();

        /// <summary>
        /// Mapea sesiones + asignaciones al DTO de respuesta, con el hogar de espacio de la
        /// petición 8 (teoría virtual sin espacio propio hereda el requisito de espacio del grupo).
        /// internal (no private): reutilizado por <see cref="ReacomodarHorarioService"/> para
        /// devolver el horario refrescado tras un movimiento parcial (P5).
        /// </summary>
        internal static List<SesionGeneradaDto> ConstruirSesionesDto(
            IReadOnlyList<Sesion> sesiones,
            IReadOnlyList<AsignacionSemanal> asignaciones,
            IReadOnlyList<Grupo> grupos,
            IReadOnlyList<BloqueTiempo> bloques)
        {
            var sesionPorId = sesiones.ToDictionary(s => s.Id);
            // Lab de origen por sesión = espacio de su asignación presencial. Permite al frontend
            // ubicar la fila virtual (EspacioId=null) en el laboratorio donde la sesión es presencial.
            var espacioHogarPorSesion = asignaciones
                .Where(a => a.Modalidad == Modalidad.Presencial && a.EspacioId.HasValue)
                .GroupBy(a => a.SesionId)
                .ToDictionary(g => g.Key, g => g.First().EspacioId!.Value.ToString());

            // Petición 8: una teoría virtual nunca tiene asignación presencial (nunca alterna) — sin
            // este fallback su fila queda sin hogar y desaparece de una grilla orientada a espacios.
            // Se rellena desde el requisito de espacio del grupo (A2), si declaró uno concreto.
            var requisitosPorGrupoDto = grupos.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().RequisitosEspacio);
            foreach (var sesion in sesiones)
            {
                if (espacioHogarPorSesion.ContainsKey(sesion.Id)) continue;
                if (!sesion.GrupoId.HasValue || !requisitosPorGrupoDto.TryGetValue(sesion.GrupoId.Value, out var reqs)) continue;
                var requisito = reqs.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(sesion));
                if (requisito?.EspacioId is Guid espacioHogar)
                    espacioHogarPorSesion[sesion.Id] = espacioHogar.ToString();
            }

            var bloquePorId = bloques.ToDictionary(b => b.Id);
            var dtos = asignaciones
                .Where(a => sesionPorId.ContainsKey(a.SesionId))
                .Select(a => MapearSesionDto(
                    a, sesionPorId[a.SesionId], bloquePorId,
                    espacioHogarPorSesion.GetValueOrDefault(a.SesionId)))
                .ToList();

            // Contraparte virtual DERIVADA: una sesión que alterna se sigue dictando la semana
            // contraria, en línea y en la misma franja. No se persiste (una asignación virtual no
            // reserva aula, así que en BD sería ruido); se genera aquí para que la grilla pueda
            // dibujarla como sub-caja dentro de la celda del aula que su pareja ocupa esa semana.
            foreach (var a in asignaciones.Where(a => sesionPorId.ContainsKey(a.SesionId)).ToList())
            {
                var sesion = sesionPorId[a.SesionId];
                if (sesion.Alternancia == TipoAlternancia.SinAlternancia) continue;
                if (a.Modalidad != Modalidad.Presencial) continue;

                var semanaOpuesta = a.Semana == SemanaAcademica.A ? SemanaAcademica.B : SemanaAcademica.A;
                var contraparte = new AsignacionSemanal(
                    Guid.NewGuid(), sesion.Id, semanaOpuesta, a.BloqueTiempoId, null, Modalidad.Virtual);

                var dto = MapearSesionDto(contraparte, sesion, bloquePorId,
                    espacioHogarPorSesion.GetValueOrDefault(sesion.Id));
                dto.EsContraparteVirtual = true;
                dtos.Add(dto);
            }

            return dtos;
        }

        internal static SesionGeneradaDto MapearSesionDto(
            AsignacionSemanal a, Sesion s, IReadOnlyDictionary<Guid, BloqueTiempo> bloquePorId, string? espacioIdHogar)
        {
            bloquePorId.TryGetValue(a.BloqueTiempoId, out var bloque);

            string horaInicio = "07:00";
            string horaFin    = "09:00";
            string dia        = "lunes";

            if (bloque != null)
            {
                dia        = DiaToString(bloque.Dia);
                horaInicio = bloque.HoraInicio.ToString("HH:mm");
                // HoraFin = HoraInicio + DuracionHoras (la duración es input fijo, no la del bloque atómico).
                horaFin    = bloque.HoraInicio.AddHours((double)s.DuracionHoras).ToString("HH:mm");
            }

            return new SesionGeneradaDto
            {
                Id            = s.Id.ToString(),
                AsignaturaId  = s.AsignaturaId.ToString(),
                DocenteId     = s.DocenteId?.ToString() ?? string.Empty,
                GrupoId       = s.GrupoId?.ToString() ?? string.Empty,
                EspacioId     = a.EspacioId?.ToString(),
                EspacioIdHogar = espacioIdHogar ?? a.EspacioId?.ToString(),
                Dia           = dia,
                HoraInicio    = horaInicio,
                HoraFin       = horaFin,
                DuracionHoras = s.DuracionHoras,
                Alternancia   = s.Alternancia.ToString(),
                Virtual       = a.Modalidad == Modalidad.Virtual,
                // Vacío cuando no alterna: la sesión se dicta igual todas las semanas, y decir "A"
                // haría creer al frontend que la semana B es distinta.
                Semana        = s.Alternancia == TipoAlternancia.SinAlternancia ? string.Empty : a.Semana.ToString(),
                ParejaId      = s.ParejaAlternanciaId?.ToString() ?? string.Empty,
                TipoFlujo     = s.TipoFlujo.ToString(),
                MotivoConflicto = s.MotivoConflicto
            };
        }

        // internal (no private) para verificación directa del mapeo en SOEA.Tests (B4 auditoría).
        internal static ConfiguracionOptimizacion MapearConfiguracion(ConfiguracionAlgoritmoDto? dto) =>
            dto is null
                ? new ConfiguracionOptimizacion()
                : new ConfiguracionOptimizacion(
                    TamañoPoblacion:      dto.TamañoPoblacion,
                    MaxGeneraciones:      dto.MaxGeneraciones,
                    ProbabilidadMutacion: dto.ProbabilidadMutacion,
                    ProbabilidadCruce:    dto.ProbabilidadCruce,
                    UmbralConvergencia:   dto.UmbralConvergencia,
                    PesoErgo:             dto.PesoErgo,
                    PesoTiempos:          dto.PesoTiempos,
                    PesoMaxHorasSeguidas: dto.PesoMaxHorasSeguidas,
                    PesoBalanceSemanas:   dto.PesoBalanceSemanas,
                    PesoPresencialFirst:  dto.PesoPresencialFirst,
                    Semilla:              dto.Semilla);

        /// <summary>
        /// Convierte los GrupoDtos del request a entidades de dominio Grupo. La disponibilidad
        /// (HC-G01) y los requisitos de espacio (HC-S03/HC-S05) quedan derivables bajo demanda
        /// desde <see cref="Grupo.ObtenerDisponibilidadSemanal"/> y <see cref="Grupo.RequisitosEspacio"/>.
        /// internal (no private) para verificación directa del mapeo en SOEA.Tests, y porque un
        /// grupo con Id inválido antes se descartaba en silencio (G3 auditoría) — ahora queda
        /// una advertencia nombrada, no oculta detrás de un identificador que el usuario no lee.
        /// </summary>
        internal static (List<Grupo> grupos, List<string> advertencias) MapearGrupos(List<GrupoDto> dtos)
        {
            var grupos = new List<Grupo>();
            var advertencias = new List<string>();
            foreach (var dto in dtos)
            {
                if (!Guid.TryParse(dto.Id, out var id))
                {
                    advertencias.Add($"[WARN] Grupo '{dto.Nombre}' tiene un Id inválido y no se incluyó en el horario.");
                    continue;
                }

                Guid? asigId    = Guid.TryParse(dto.AsignaturaId, out var aid) ? aid : null;
                Guid? facId     = Guid.TryParse(dto.FacultadId,   out var fid) ? fid : null;
                Guid? docenteId = Guid.TryParse(dto.DocenteId,    out var did) ? did : null;

                var grupo = new Grupo(
                    id:                   id,
                    nombre:               dto.Nombre,
                    programaId:           Guid.Empty,   // no requerido para el pipeline
                    estudiantesInscritos: Math.Max(1, dto.EstudiantesInscritos),
                    codigo:               dto.Codigo,
                    asignaturaId:         asigId,
                    facultadId:           facId,
                    docenteId:            docenteId);

                grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
                grupo.ActualizarRequisitosEspacio(MapearRequisitosEspacio(dto.RequisitosEspacio));
                grupos.Add(grupo);
            }
            return (grupos, advertencias);
        }

        private static List<RequisitoEspacio> MapearRequisitosEspacio(List<RequisitoEspacioDto> dtos) =>
            dtos.Select(d => new RequisitoEspacio(
                TipoSesion:  ParseTipoSesion(d.TipoSesion),
                EspacioId:   Guid.TryParse(d.EspacioId, out var eid) ? eid : null,
                TipoEspacio: ParseTipoEspacioRequisito(d.TipoEspacio),
                Sesiones:    d.Sesiones))
            .ToList();

        private static TipoSesion ParseTipoSesion(string? tipo) => tipo?.Trim().ToLowerInvariant() switch
        {
            "teoriapresencial" => TipoSesion.TeoriaPresencial,
            "teoriavirtual"    => TipoSesion.TeoriaVirtual,
            "laboratorio"      => TipoSesion.Laboratorio,
            _                  => TipoSesion.TeoriaPresencial
        };

        internal static string DiaToString(DiaDeSemana dia) => dia switch
        {
            DiaDeSemana.Lunes     => "lunes",
            DiaDeSemana.Martes    => "martes",
            DiaDeSemana.Miercoles => "miercoles",
            DiaDeSemana.Jueves    => "jueves",
            DiaDeSemana.Viernes   => "viernes",
            DiaDeSemana.Sábado    => "sabado",
            _ => "lunes"
        };

        private static TipoEspacio ParseTipoEspacio(string? tipo) => tipo?.ToLower() switch
        {
            "laboratorio" => TipoEspacio.Laboratorio,
            "auditorio"   => TipoEspacio.Auditorio,
            _             => TipoEspacio.Salon
        };

        /// <summary>
        /// M6: distinto de <see cref="ParseTipoEspacio"/> — ese parsea el tipo de un ESPACIO real
        /// (siempre tiene uno, default conservador Salon). Este parsea el tipo de un REQUISITO de
        /// grupo, donde ausente/no reconocido significa "sin preferencia de tipo" (null), no Salon.
        /// </summary>
        private static TipoEspacio? ParseTipoEspacioRequisito(string? tipo) => tipo?.Trim().ToLowerInvariant() switch
        {
            "laboratorio" => TipoEspacio.Laboratorio,
            "auditorio"   => TipoEspacio.Auditorio,
            "salon"       => TipoEspacio.Salon,
            _             => null
        };

        private static TimeOnly? ParseHora(string? hhmm) =>
            !string.IsNullOrWhiteSpace(hhmm) && TimeOnly.TryParse(hhmm, out var t) ? t : null;

        private static CategoriaAsignatura ParseCategoria(string? categoria) =>
            categoria?.Trim().ToLowerInvariant() switch
            {
                "optativa"   => CategoriaAsignatura.Optativa,
                "electiva"   => CategoriaAsignatura.Electiva,
                _            => CategoriaAsignatura.Obligatoria   // conservador: si no se especifica → Obligatoria
            };

        private static TipoFlujo ParseTipoFlujo(string? tipoFlujo) =>
            tipoFlujo?.Trim().ToLowerInvariant() switch
            {
                "aulavirtual" => TipoFlujo.AulaVirtual,
                _             => TipoFlujo.Laboratorio   // default histórico: horario base pre-desglose
            };

        // ── Cesión a alternancia (activación de la Semana B) ────────────────────────────
        //
        // Reactiva por diseño: no se cede nada hasta que CP-SAT declara infactibilidad POR ESPACIO.
        // Antes existía además una heurística preventiva (AplicarPrioridadPresencial) que cedía
        // ANTES de intentar generar, comparando una estimación agregada de horas contra aulas, y
        // que como último recurso virtualizaba sesiones de forma permanente. Se eliminó: contradecía
        // la regla de negocio ("la Semana B se activa después de comprobar que no hay configuración
        // válida") y convertía materias presenciales en virtuales sin que nadie lo aprobara. El
        // pre-chequeo de demanda vs capacidad de CP-SAT ya devuelve Espacio sin resolver, así que el
        // desbordamiento aritmético también entra por esta vía.

        /// <summary>
        /// Contexto de emparejamiento, construido UNA vez por generación. Los dominios y las aulas
        /// candidatas salen de las mismas fuentes que usará CP-SAT
        /// (<see cref="CalculadorDominioSesion"/> y <see cref="CalculadorEspaciosSesion"/>): esa
        /// identidad es lo que garantiza que una pareja aceptada siempre tenga al menos un
        /// (bloque, aula) donde el solver pueda colocarla.
        /// </summary>
        internal sealed record ContextoCesion(
            IReadOnlyDictionary<Guid, Grupo> GrupoPorId,
            IReadOnlyDictionary<Guid, int[]> DominioPorSesion,
            IReadOnlyDictionary<Guid, IReadOnlyList<int>> AulasPorSesion);

        /// <summary>Pareja cedida, o el motivo por el que no se encontró ninguna.</summary>
        internal sealed record ResultadoCesion(Sesion? S1, Sesion? S2, IReadOnlyList<string> Diagnostico)
        {
            public bool Cedio => S1 is not null && S2 is not null;
        }

        internal static ContextoCesion ConstruirContextoCesion(
            List<Sesion> sesiones,
            List<Espacio> espacios,
            List<Grupo> grupos,
            List<BloqueTiempo> bloques,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)> ventanaPorAsignatura)
        {
            var grupoPorId = grupos.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
            var rangos = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            var permitidosPorGrupo = new Dictionary<Guid, HashSet<int>>();
            foreach (var grupo in grupoPorId.Values)
            {
                if (grupo.Id == Guid.Empty) continue;
                var permitidos = CalculadorDominioSesion.BloquesPermitidos(bloques, grupo.ObtenerDisponibilidadSemanal());
                if (permitidos is not null) permitidosPorGrupo[grupo.Id] = permitidos;
            }

            var dominios = new Dictionary<Guid, int[]>();
            var aulas = new Dictionary<Guid, IReadOnlyList<int>>();
            foreach (var sesion in sesiones)
            {
                int dur = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));

                HashSet<int>? permGrupo = null;
                if (sesion.GrupoId.HasValue) permitidosPorGrupo.TryGetValue(sesion.GrupoId.Value, out permGrupo);
                (TimeOnly? min, TimeOnly? max) ventana = default;
                ventanaPorAsignatura.TryGetValue(sesion.AsignaturaId, out ventana);
                dominios[sesion.Id] = CalculadorDominioSesion.StartsPermitidos(
                    dur, bloques, rangos, diaPorIdx, permGrupo, ventana.min, ventana.max);

                RequisitoEspacio? requisito = sesion.GrupoId.HasValue &&
                    grupoPorId.TryGetValue(sesion.GrupoId.Value, out var g)
                    ? g.RequisitosEspacio.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(sesion))
                    : null;
                int estudiantes = sesion.GrupoId.HasValue && grupoPorId.TryGetValue(sesion.GrupoId.Value, out var g2)
                    ? g2.EstudiantesInscritos : 0;

                aulas[sesion.Id] = CalculadorEspaciosSesion.Candidatos(sesion, espacios, requisito)
                    .Where(e => estudiantes == 0 || espacios[e].Capacidad >= estudiantes)
                    .ToList();
            }

            return new ContextoCesion(grupoPorId, dominios, aulas);
        }

        /// <summary>
        /// Cede UNA pareja de sesiones a alternancia: una queda presencial en semana A y virtual en
        /// B, la otra al revés, compartiendo bloque y aula (HC-ALT). Es el único mecanismo que libera
        /// capacidad — una sesión que no alterna ocupa su aula en las dos semanas.
        ///
        /// Candidata: presencial (laboratorio o teoría, sin distinción — antes la vía reactiva solo
        /// aceptaba laboratorios y la preventiva solo teoría), sin alternancia, no bloqueada, no fija,
        /// que cumpla al menos un criterio de elegibilidad activo (Electiva / Optativa / Elegible;
        /// MultiplesSesiones solo desempata el orden) y que deje al menos otra sesión presencial de su
        /// (asignatura, grupo) sin ceder.
        /// </summary>
        internal static ResultadoCesion CederSiguientePareja(
            List<Sesion> sesiones,
            IReadOnlyList<(CriterioElegibilidadAlternancia Criterio, Func<Sesion, bool> Predicado)> criterios,
            HashSet<Guid> sesionesFijasIds,
            ContextoCesion ctx,
            HashSet<(Guid, Guid)> parejasDescartadas)
        {
            var sinPareja = new List<string>();

            var candidatos = sesiones.Where(s =>
                s.Modalidad == Modalidad.Presencial &&
                s.Alternancia == TipoAlternancia.SinAlternancia &&
                !s.Bloqueada &&
                !sesionesFijasIds.Contains(s.Id)).ToList();

            if (candidatos.Count < 2)
                return new ResultadoCesion(null, null, new[]
                {
                    "No quedan sesiones presenciales que puedan alternar: se necesitan al menos dos para " +
                    "formar una pareja que comparta aula en semanas alternas."
                });

            // Hermanas presenciales de la misma (asignatura, grupo). NO es un veto: ceder ya no
            // virtualiza nada — la sesión sigue siendo presencial, una semana de cada dos, que es
            // justamente el modelo de alternancia de la institución. Vetar la última sesión de una
            // materia (como hacía la vía anterior, heredada de cuando ceder significaba volverla
            // virtual para siempre) dejaba sin salida a los horarios que la alternancia sí resuelve.
            // Se usa como PREFERENCIA: primero cede quien tiene hermanas.
            var hermanasPorAsigYGrupo = candidatos
                .GroupBy(s => (s.AsignaturaId, s.GrupoId))
                .ToDictionary(g => g.Key, g => g.Count());
            int Hermanas(Sesion s) => hermanasPorAsigYGrupo[(s.AsignaturaId, s.GrupoId)];

            // MultiplesSesiones no otorga elegibilidad por sí solo (evita que CUALQUIER asignatura con
            // 2+ sesiones se vuelva candidata): solo desempata el orden entre quienes ya matchean un
            // criterio real.
            bool EsElegible(Sesion s) => criterios.Any(c =>
                c.Criterio != CriterioElegibilidadAlternancia.MultiplesSesiones && c.Predicado(s));

            int CriterioRank(Sesion s)
            {
                for (int i = 0; i < criterios.Count; i++)
                    if (criterios[i].Predicado(s)) return i;
                return int.MaxValue;
            }

            var elegibles = candidatos.Where(EsElegible)
                                      .OrderBy(CriterioRank)
                                      .ThenByDescending(Hermanas)
                                      .ToList();
            if (elegibles.Count == 0)
                return new ResultadoCesion(null, null, new[]
                {
                    "Ninguna sesión presencial cumple los criterios de cesión configurados. Marque más " +
                    "asignaturas como candidatas a alternancia en el catálogo, o active más criterios de cesión."
                });

            var vacio = Array.Empty<int>();
            IReadOnlyList<int> Aulas(Sesion s) => ctx.AulasPorSesion.TryGetValue(s.Id, out var a) ? a : vacio;
            int[] Dominio(Sesion s) => ctx.DominioPorSesion.TryGetValue(s.Id, out var d) ? d : vacio;

            foreach (var s1 in elegibles)
            {
                var motivos = new List<MotivoRechazoPareja>();
                foreach (var s2 in elegibles)
                {
                    if (ReferenceEquals(s1, s2)) continue;
                    if (parejasDescartadas.Contains(ClaveDePareja(s1.Id, s2.Id))) continue;

                    var motivo = EvaluadorParejaAlternancia.Evaluar(
                        s1, s2, ctx.GrupoPorId, Dominio(s1), Dominio(s2), Aulas(s1), Aulas(s2));
                    if (motivo != MotivoRechazoPareja.Ninguno) { motivos.Add(motivo); continue; }

                    var pareja = Guid.NewGuid();
                    s1.AplicarAlternancia(TipoAlternancia.TipoA, TipoAlternanciaConfig.IdTipoA,
                        cedidaPorSaturacion: true, parejaAlternanciaId: pareja);
                    s2.AplicarAlternancia(TipoAlternancia.TipoB, TipoAlternanciaConfig.IdTipoB,
                        cedidaPorSaturacion: true, parejaAlternanciaId: pareja);
                    return new ResultadoCesion(s1, s2, Array.Empty<string>());
                }

                if (motivos.Count > 0)
                    sinPareja.Add($"{DescribirSesion(s1, ctx)}: {ExplicarRechazo(motivos)}");
            }

            return new ResultadoCesion(null, null, sinPareja.Count > 0
                ? sinPareja
                : new[] { "No hay ninguna pareja de sesiones que pueda alternar entre las candidatas disponibles." });
        }

        /// <summary>Clave simétrica de pareja: (a,b) y (b,a) son la misma.</summary>
        internal static (Guid, Guid) ClaveDePareja(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        private static string DescribirSesion(Sesion s, ContextoCesion ctx)
        {
            var grupo = s.GrupoId.HasValue && ctx.GrupoPorId.TryGetValue(s.GrupoId.Value, out var g)
                ? g.Nombre : "grupo sin nombre";
            return $"la sesión de {CalculadorEspaciosSesion.TipoSesionDe(s)} de '{grupo}'";
        }

        /// <summary>Traduce el motivo más frecuente a lenguaje del coordinador.</summary>
        private static string ExplicarRechazo(List<MotivoRechazoPareja> motivos)
        {
            var motivo = motivos.GroupBy(m => m).OrderByDescending(g => g.Count()).First().Key;
            return motivo switch
            {
                MotivoRechazoPareja.SinFranjaComun =>
                    "no comparte ninguna franja válida con otra candidata (la disponibilidad del grupo o la " +
                    "ventana horaria de la asignatura no se solapan). Amplíe una de las dos.",
                MotivoRechazoPareja.SinAulaComun =>
                    "no hay ningún aula que sirva a las dos sesiones (tipo de espacio, espacio fijo o aforo). " +
                    "Añada un aula compatible o revise los requisitos de espacio del grupo.",
                MotivoRechazoPareja.DuracionDistinta =>
                    "ninguna otra candidata tiene su misma duración; una pareja comparte el mismo bloque.",
                MotivoRechazoPareja.MismoGrupo =>
                    "solo quedan candidatas de su mismo grupo, y una cohorte no puede tener dos sesiones a la misma hora.",
                MotivoRechazoPareja.MismaAsignatura =>
                    "solo quedan candidatas de su misma asignatura; alternar tiene sentido entre materias distintas.",
                MotivoRechazoPareja.MismaSesion =>
                    "es la única candidata: hacen falta dos sesiones para formar una pareja.",
                MotivoRechazoPareja.RequisitoIncompatible =>
                    "su requisito de espacio no coincide con el de ninguna otra candidata.",
                _ => "no encontró ninguna pareja compatible."
            };
        }
    }
}
