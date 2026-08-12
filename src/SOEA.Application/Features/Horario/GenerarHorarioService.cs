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
        private readonly ICriterioCesionAlternanciaRepositorio _criterioCesionRepo;
        private readonly IUnitOfWork                 _uow;

        public GenerarHorarioService(
            IMotorColoracionGrafo       fase1,
            IMotorConstraintProgramming  fase2,
            IMotorGenetico              fase3,
            IHorarioRepositorio         horarioRepo,
            ISesionRepositorio          sesionRepo,
            IAsignacionSemanalRepositorio asignacionRepo,
            ICriterioCesionAlternanciaRepositorio criterioCesionRepo,
            IUnitOfWork                 uow)
        {
            _fase1       = fase1;
            _fase2       = fase2;
            _fase3       = fase3;
            _horarioRepo = horarioRepo;
            _sesionRepo  = sesionRepo;
            _asignacionRepo = asignacionRepo;
            _criterioCesionRepo = criterioCesionRepo;
            _uow         = uow;
        }

        public async Task<GenerarHorarioResponse> EjecutarAsync(GenerarHorarioRequest request, CancellationToken ct = default)
        {
            var logs = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            logs.Add($"[INFO] Iniciando pipeline de optimización con {request.Asignaturas.Count} asignaturas, {request.Docentes.Count} docentes y {request.Espacios.Count} espacios.");

            // ── 1. Convertir DTOs del frontend a entidades de dominio ───────────
            var espacios  = MapearEspacios(request.Espacios);
            var bloques   = GenerarBloquesTiempo();
            var docentes  = MapearDocentes(request.Docentes, bloques);
            // Multi-grupo real: cada grupo lleva su propio Id como GrupoId de sus sesiones, así que
            // HC-C01 (solape) y HC-G01 (franja) se aplican por grupo, no sobre un id sintético
            // compartido por todo el run.
            var grupos    = MapearGrupos(request.Grupos);

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

            var sesiones = MapearSesionesIniciales(grupos, request.Asignaturas);
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

            // ── 1c. Etapa 1 (teoría, heurística) — presencial-first: ceder por prioridad si hay
            // saturación estimada. Candidatas: sesiones de teoría que matchean algún criterio activo
            // de elegibilidad (Electiva / Optativa / Elegible), nunca lo que el usuario marcó virtual
            // desde el principio. Se acumulan en sesionesCedidasEnOrden para el pase de reversión
            // post-Fase 3.
            var sesionesCedidasEnOrden = AplicarPrioridadPresencial(sesiones, espacios, predicadosCesion, grupos);
            if (sesionesCedidasEnOrden.Count > 0)
                logs.Add($"[INFO] Etapa 1: {sesionesCedidasEnOrden.Count} sesión(es) cedieron presencialidad por saturación de espacios. " +
                         $"Orden de cesión — criterios activos en orden configurado: {string.Join(" → ", criteriosActivos.Select(c => c.Criterio))}. " +
                         "Se alterna (\"Tipo C\" dinámico: presencial 1 semana) antes de virtualizar por completo.");

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
                sesionesColoreadas, bloques, espacios, docentes,
                grupos: grupos,
                sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
                ventanaPorAsignatura: ventanaPorAsig,
                ct);

            // ── Etapa 2 (labs, reactiva) — presencial-first: si Fase 2 es infactible por espacio
            // REAL (no la heurística de Etapa 1), cede candidatos de laboratorio uno a uno (misma
            // lista de criterios activos) y reintenta Fase 1/2, hasta agotar candidatos o lograr
            // factibilidad. Si la infactibilidad no es por espacio (ventana horaria, franja de
            // grupo, datos), ceder no ayuda — no entra al loop.
            while (!resultadoFactibilidad.EsFactible
                   && resultadoFactibilidad.Motivo == MotivoInfactibilidad.Espacio
                   && CederSiguienteCandidatoLab(sesiones, predicadosCesion, sesionesFijasIds, sesionesCedidasEnOrden, grupos))
            {
                sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(
                    sesiones, bloques, grupos, ventanaPorAsig, ct)).ToList();
                resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                    sesionesColoreadas, bloques, espacios, docentes,
                    grupos: grupos,
                    sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
                    ventanaPorAsignatura: ventanaPorAsig,
                    ct);
                logs.Add($"[INFO] Etapa 2: cedida sesión de laboratorio adicional por saturación de espacio real; " +
                         $"reintentando Fase 1/2 ({sesionesCedidasEnOrden.Count} sesión(es) cedidas en total).");
            }
            swFase2.Stop();

            if (!resultadoFactibilidad.EsFactible)
            {
                logs.Add($"[ERROR] Fase 2 falló en {swFase2.ElapsedMilliseconds}ms: {resultadoFactibilidad.MensajeError}");
                return new GenerarHorarioResponse
                {
                    HorarioId     = Guid.NewGuid(),
                    Semestre      = request.Semestre,
                    EsFactible    = false,
                    MensajeError  = resultadoFactibilidad.MensajeError,
                    Logs          = logs,
                    Sesiones      = new List<SesionGeneradaDto>()
                };
            }
            logs.Add($"[INFO] Fase 2 completada exitosamente en {swFase2.ElapsedMilliseconds}ms.");

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
            // Verificamos las asignaciones FINALES contra las MISMAS 7 reglas que CP-SAT impuso
            // en Fase 2: HC-C01/HC-S01 (solapes) + HC-VH/HC-G01/HC-CAP/HC-S03/HC-S05 (contexto).
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
                    .ToDictionary(g => g.Key, g => g.First().RequisitosEspacio));

            var conflictos = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex, contextoValidacion);
            if (conflictos.Count > 0)
            {
                logs.Add($"[WARN] Post-chequeo detectó {conflictos.Count} violación(es) en la salida del GA; usando la solución de Fase 2.");
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
        /// Post-chequeo de restricciones duras sobre las asignaciones finales: las mismas 7 reglas
        /// que CP-SAT impone en Fase 2 — HC-C01, HC-S01 y (vía contexto) HC-VH, HC-G01, HC-CAP,
        /// HC-S03, HC-S05. El docente está fuera del pipeline (CR-08), así que no se valida carga
        /// semanal de docente (HC-I03).
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

                var asigId  = Guid.TryParse(dto.AsignaturaId, out var aid) ? aid : Guid.NewGuid();
                Guid? espId = Guid.TryParse(dto.EspacioId, out var eid) ? eid : null;

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
                    docenteId: null,
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

        private static List<Espacio> MapearEspacios(List<EspacioDto> dtos) =>
            dtos.Select(dto => new Espacio(
                id: Guid.TryParse(dto.Id, out var eid) ? eid : Guid.NewGuid(),
                nombre: dto.Nombre,
                tipo: ParseTipoEspacio(dto.Tipo),
                capacidad: dto.Capacidad > 0 ? dto.Capacidad : 30
            )).ToList();

        private static List<Docente> MapearDocentes(List<DocenteDto> dtos, List<BloqueTiempo> bloques)
        {
            return dtos.Select(dto =>
            {
                var id = Guid.TryParse(dto.Id, out var did) ? did : Guid.NewGuid();
                var maxHoras = dto.MaxHoras.HasValue && dto.MaxHoras > 0 ? dto.MaxHoras.Value : 20m;

                // Fuente única del parseo por día (A1): sin información → sin restricción (todos los
                // bloques); día explícitamente no-disponible → cerrado; el resto respeta su ventana.
                var disponibilidad = DisponibilidadSemanal.Desde(MapearEntradasCrudas(dto.Disponibilidad));
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
        /// </summary>
        private static List<Sesion> MapearSesionesIniciales(
            List<Grupo> grupos,
            List<AsignaturaDto> asignaturasDtos)
        {
            var sesiones = new List<Sesion>();
            // Bloque placeholder — Fase 1 lo reemplazará
            var bloqueTemp = Guid.NewGuid();
            var asignaturaPorId = asignaturasDtos
                .Where(dto => Guid.TryParse(dto.Id, out _))
                .ToDictionary(dto => Guid.Parse(dto.Id));

            foreach (var grupo in grupos)
            {
                if (grupo.AsignaturaId is not { } asigId || !asignaturaPorId.TryGetValue(asigId, out var dto))
                    continue;

                // CR-08 / presencial-first: el docente sale del pipeline (se asigna DESPUÉS de
                // generar el horario). Las sesiones se generan sin docente; el eje de conflicto
                // y de optimización es la cohorte (GrupoId), no el docente.

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
                            docenteId: null,
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
            return sesiones;
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
            return asignaciones
                .Where(a => sesionPorId.ContainsKey(a.SesionId))
                .Select(a => MapearSesionDto(
                    a, sesionPorId[a.SesionId], bloquePorId,
                    espacioHogarPorSesion.GetValueOrDefault(a.SesionId)))
                .ToList();
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
                EspacioId     = a.EspacioId?.ToString(),
                EspacioIdHogar = espacioIdHogar ?? a.EspacioId?.ToString(),
                Dia           = dia,
                HoraInicio    = horaInicio,
                HoraFin       = horaFin,
                DuracionHoras = s.DuracionHoras,
                Alternancia   = s.Alternancia.ToString(),
                Virtual       = a.Modalidad == Modalidad.Virtual,
                Semana        = a.Semana.ToString(),
                TipoFlujo     = s.TipoFlujo.ToString()
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
        /// </summary>
        private static List<Grupo> MapearGrupos(List<GrupoDto> dtos)
        {
            var grupos = new List<Grupo>();
            foreach (var dto in dtos)
            {
                if (!Guid.TryParse(dto.Id, out var id)) continue;

                Guid? asigId = Guid.TryParse(dto.AsignaturaId, out var aid) ? aid : null;
                Guid? facId  = Guid.TryParse(dto.FacultadId,   out var fid) ? fid : null;

                var grupo = new Grupo(
                    id:                   id,
                    nombre:               dto.Nombre,
                    programaId:           Guid.Empty,   // no requerido para el pipeline
                    estudiantesInscritos: Math.Max(1, dto.EstudiantesInscritos),
                    codigo:               dto.Codigo,
                    asignaturaId:         asigId,
                    facultadId:           facId);

                grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
                grupo.ActualizarRequisitosEspacio(MapearRequisitosEspacio(dto.RequisitosEspacio));
                grupos.Add(grupo);
            }
            return grupos;
        }

        private static List<RequisitoEspacio> MapearRequisitosEspacio(List<RequisitoEspacioDto> dtos) =>
            dtos.Select(d => new RequisitoEspacio(
                TipoSesion:  ParseTipoSesion(d.TipoSesion),
                EspacioId:   Guid.TryParse(d.EspacioId, out var eid) ? eid : null,
                TipoEspacio: ParseTipoEspacio(d.TipoEspacio),
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

        /// <summary>
        /// A2/A4 (VERIFICA punto 3 — atomicidad por espacio): dos sesiones son pareja válida de
        /// alternancia solo si un mismo espacio físico les sirve a ambas: misma duración y, si
        /// alguna declara un requisito de espacio concreto (grupo.RequisitosEspacio), el mismo
        /// requisito en las dos (o ninguna lo declara, y comparten el default por TipoSesion).
        /// </summary>
        private static bool SonParejaCompatible(
            Sesion a, Sesion b, IReadOnlyDictionary<Guid, Grupo> grupoPorId)
        {
            if (a.DuracionHoras != b.DuracionHoras) return false;

            RequisitoEspacio? RequisitoDe(Sesion s) =>
                s.GrupoId.HasValue && grupoPorId.TryGetValue(s.GrupoId.Value, out var g)
                    ? g.RequisitosEspacio.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(s))
                    : null;

            var ra = RequisitoDe(a);
            var rb = RequisitoDe(b);
            if (ra is null && rb is null) return true;
            if (ra is null || rb is null) return false; // una declara requisito, la otra no ⇒ no garantizado
            return ra.EspacioId == rb.EspacioId && ra.TipoEspacio == rb.TipoEspacio;
        }

        /// <summary>
        /// Etapa 1 (teoría, heurística pre-Fase 1): cuando la demanda presencial supera la
        /// capacidad estimada de espacios, cede sesiones candidatas — aquellas que matchean AL MENOS
        /// UN criterio activo de <paramref name="predicadosCesion"/>, en el orden de esa lista.
        /// Una sesión que no matchea ningún criterio activo NUNCA es candidata (no hay regla
        /// implícita por categoría). Nunca toca lo que el usuario marcó virtual desde el principio.
        /// VERIFICA: la alternancia se aplica EN PAREJAS (dos sesiones, normalmente de asignaturas
        /// distintas, con espacio compatible — <see cref="SonParejaCompatible"/>) que comparten un
        /// <see cref="Sesion.ParejaAlternanciaId"/> (A4); un candidato sin pareja no alterna, cae al
        /// Pase 2 (virtualización). Devuelve los IDs de las sesiones cedidas, EN ORDEN de cesión.
        /// </summary>
        // internal (no private) para verificación directa de la prioridad de cesión en SOEA.Tests.
        internal static List<Guid> AplicarPrioridadPresencial(
            List<Sesion> sesiones,
            List<Espacio> espacios,
            IReadOnlyList<(CriterioElegibilidadAlternancia Criterio, Func<Sesion, bool> Predicado)> criterios,
            List<Grupo>? grupos = null)
        {
            var cedidasIds = new List<Guid>();
            if (espacios.Count == 0 || criterios.Count == 0) return cedidasIds;

            var grupoPorId = (grupos ?? new List<Grupo>()).ToDictionary(g => g.Id);

            // Capacidad máxima estimada: nro_espacios × días × horas_útiles. Heurística de pre-pase;
            // el gate duro real es CP-SAT (HC-CAP / demanda-vs-capacidad por semana).
            int capacidadMaxEstimada = espacios.Count * 5 * 8;
            int Horas(Sesion s) => Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));

            int demandaHoras = sesiones.Where(s => s.Modalidad == Modalidad.Presencial).Sum(Horas);
            int excesohoras  = demandaHoras - capacidadMaxEstimada;
            if (excesohoras <= 0) return cedidasIds; // sin saturación → no tocar nada

            // Sesiones por asignatura (un run = un grupo ⇒ = sesiones/semana).
            var totalPorAsig = sesiones
                .GroupBy(s => s.AsignaturaId)
                .ToDictionary(g => g.Key, g => g.Count());

            // MultiplesSesiones no otorga elegibilidad por sí solo (evita que CUALQUIER asignatura con
            // 2+ sesiones se vuelva candidata) — solo desempata el orden entre quienes ya matchean un
            // criterio de elegibilidad real (Electiva/Optativa/Elegible).
            var criteriosElegibilidad = criterios
                .Where(c => c.Criterio != CriterioElegibilidadAlternancia.MultiplesSesiones)
                .ToList();
            bool EsCandidata(Sesion s) => criteriosElegibilidad.Any(c => c.Predicado(s));

            // El primer criterio activo (en el orden configurado) que matchea gana — cede primero.
            // "MultiplesSesiones" participa aquí como desempate (si está activo y en primer lugar,
            // reproduce la prioridad estructural: 2+ sesiones antes que sesión única).
            int CriterioRank(Sesion s)
            {
                for (int i = 0; i < criterios.Count; i++)
                    if (criterios[i].Predicado(s)) return i;
                return int.MaxValue;
            }

            List<Sesion> OrdenarCesion(IEnumerable<Sesion> ss) => ss.OrderBy(CriterioRank).ToList();

            // ── Pase 1 — "Tipo C" dinámico: alternar EN PAREJAS (presencial 1 semana cada una, en
            // semanas opuestas) en vez de virtualizar. Solo asignaturas con ≥2 sesiones y conservando
            // SIEMPRE ≥1 sesión presencial pura por asignatura. Respeta Bloqueada. Excluye
            // TipoFlujo.Laboratorio: la Etapa 2 (reactiva) se encarga de los labs.
            var presencialesPurasPorAsig = new Dictionary<Guid, int>(totalPorAsig);
            var candidatasPase1 = OrdenarCesion(sesiones.Where(s =>
                s.Modalidad == Modalidad.Presencial &&
                s.TipoFlujo != TipoFlujo.Laboratorio &&
                s.Alternancia == TipoAlternancia.SinAlternancia &&
                !s.Bloqueada &&
                EsCandidata(s)));

            bool PuedeCeder(Sesion s) =>
                totalPorAsig.TryGetValue(s.AsignaturaId, out var n) && n >= 2 &&
                presencialesPurasPorAsig[s.AsignaturaId] > 1;

            var usadasPase1 = new HashSet<Guid>();
            for (int i = 0; i < candidatasPase1.Count && excesohoras > 0; i++)
            {
                var s1 = candidatasPase1[i];
                if (usadasPase1.Contains(s1.Id) || !PuedeCeder(s1)) continue;

                // Pareja: otra candidata SIN pareja aún, de OTRA asignatura (alternar tiene sentido
                // cuando dos asignaturas se turnan el mismo espacio), con espacio compatible.
                Sesion? s2 = null;
                for (int j = i + 1; j < candidatasPase1.Count; j++)
                {
                    var cand = candidatasPase1[j];
                    if (usadasPase1.Contains(cand.Id) || cand.AsignaturaId == s1.AsignaturaId) continue;
                    if (!PuedeCeder(cand) || !SonParejaCompatible(s1, cand, grupoPorId)) continue;
                    s2 = cand;
                    break;
                }
                if (s2 is null) continue; // sin pareja: no alterna (VERIFICA) — cae al Pase 2

                var patron = Guid.NewGuid();
                s1.AplicarAlternancia(TipoAlternancia.TipoA, TipoAlternanciaConfig.IdTipoA, cedidaPorSaturacion: true, parejaAlternanciaId: patron);
                s2.AplicarAlternancia(TipoAlternancia.TipoB, TipoAlternanciaConfig.IdTipoB, cedidaPorSaturacion: true, parejaAlternanciaId: patron);
                usadasPase1.Add(s1.Id); usadasPase1.Add(s2.Id);
                presencialesPurasPorAsig[s1.AsignaturaId]--;
                presencialesPurasPorAsig[s2.AsignaturaId]--;
                excesohoras -= (Horas(s1) + 1) / 2 + (Horas(s2) + 1) / 2; // cada una alterna ⇒ ~mitad de huella
                cedidasIds.Add(s1.Id); cedidasIds.Add(s2.Id);
            }

            // ── Pase 2 — último recurso: virtualización total, mismo orden de prioridad.
            // Aquí sí pueden caer las sesiones únicas (single-session) y las que no encontraron
            // pareja en el Pase 1. Excluye Laboratorio por la misma razón que el Pase 1, y excluye
            // lo que el Pase 1 ya alternó — sin este filtro, VirtualizarSesion() podría re-tocar la
            // mitad de una pareja ya formada (Modalidad→Virtual sin limpiar Alternancia/
            // ParejaAlternanciaId) y corromperla, violando HC-ALT.
            foreach (var s in OrdenarCesion(sesiones.Where(s =>
                         s.Modalidad == Modalidad.Presencial &&
                         s.TipoFlujo != TipoFlujo.Laboratorio &&
                         s.Alternancia == TipoAlternancia.SinAlternancia &&
                         !s.Bloqueada &&
                         EsCandidata(s))))
            {
                if (excesohoras <= 0) break;
                s.VirtualizarSesion(cedidaPorSaturacion: true);
                excesohoras -= Horas(s);
                cedidasIds.Add(s.Id);
            }

            return cedidasIds;
        }

        /// <summary>
        /// Etapa 2 (labs, reactiva): cede UNA PAREJA de sesiones de laboratorio candidatas cuando
        /// Fase 2 reporta infactibilidad real de espacio (VERIFICA: nunca una sesión suelta).
        /// Candidata: presencial, sin alternancia, no bloqueada, no fija, y que matchea AL MENOS UN
        /// criterio de elegibilidad activo (Electiva/Optativa/Elegible — mismo orden que Etapa 1;
        /// MultiplesSesiones se ignora aquí, no otorga elegibilidad). La pareja exige espacio
        /// compatible (<see cref="SonParejaCompatible"/>). Devuelve false si no hay pareja candidata.
        /// </summary>
        private static bool CederSiguienteCandidatoLab(
            List<Sesion> sesiones,
            IReadOnlyList<(CriterioElegibilidadAlternancia Criterio, Func<Sesion, bool> Predicado)> criterios,
            HashSet<Guid> sesionesFijasIds,
            List<Guid> sesionesCedidasEnOrden,
            List<Grupo>? grupos = null)
        {
            var grupoPorId = (grupos ?? new List<Grupo>()).ToDictionary(g => g.Id);
            var candidatos = sesiones.Where(s =>
                s.TipoFlujo == TipoFlujo.Laboratorio &&
                s.Modalidad == Modalidad.Presencial &&
                s.Alternancia == TipoAlternancia.SinAlternancia &&
                !s.Bloqueada &&
                !sesionesFijasIds.Contains(s.Id)).ToList();
            if (candidatos.Count < 2) return false;

            bool EsElegible(Sesion s) => criterios.Any(c =>
                c.Criterio != CriterioElegibilidadAlternancia.MultiplesSesiones && c.Predicado(s));

            // MultiplesSesiones no otorga elegibilidad — ver AplicarPrioridadPresencial.
            foreach (var (criterio, predicado) in criterios)
            {
                if (criterio == CriterioElegibilidadAlternancia.MultiplesSesiones) continue;
                var s1 = candidatos.FirstOrDefault(predicado);
                if (s1 is null) continue;

                var s2 = candidatos.FirstOrDefault(c =>
                    c.Id != s1.Id && EsElegible(c) && SonParejaCompatible(s1, c, grupoPorId));
                if (s2 is null) continue; // sin pareja compatible: no cede (VERIFICA)

                var patron = Guid.NewGuid();
                s1.AplicarAlternancia(TipoAlternancia.TipoA, TipoAlternanciaConfig.IdTipoA, cedidaPorSaturacion: true, parejaAlternanciaId: patron);
                s2.AplicarAlternancia(TipoAlternancia.TipoB, TipoAlternanciaConfig.IdTipoB, cedidaPorSaturacion: true, parejaAlternanciaId: patron);
                sesionesCedidasEnOrden.Add(s1.Id);
                sesionesCedidasEnOrden.Add(s2.Id);
                return true;
            }
            return false;
        }
    }
}
