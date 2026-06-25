using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using CpDomain = Google.OrTools.Util.Domain;
#pragma warning disable CA1860 // Evitar usar el método 'Contains' de Enumerable -- se usa HashSet<int>

namespace SOEA.Engine.ConstraintProg
{
    /// <summary>
    /// Fase 2 — Constraint Programming con OR-Tools CP-SAT.
    /// Modela cada sesión como DOS intervalos de longitud fija = DuracionHoras, uno por
    /// <see cref="SemanaAcademica"/> (A / B), e impone NoOverlap por (docente, semana) y por
    /// (espacio, semana). La modalidad por semana se DERIVA de la alternancia (dato fijo):
    /// TipoA → presencial en A / virtual en B; TipoB → virtual en A / presencial en B;
    /// SinAlternancia → presencial en ambas. Para TipoA/TipoB la franja virtual se enlaza a la
    /// presencial (regla 9: misma franja). La duración es inmutable (CLAUDE.md regla 6).
    /// </summary>
    public class MotorConstraintProgramming : IMotorConstraintProgramming
    {
        private readonly ILogger<MotorConstraintProgramming> _logger;
        private readonly CpSatOptions _options;

        private static readonly SemanaAcademica[] Semanas = { SemanaAcademica.A, SemanaAcademica.B };

        /// <summary>
        /// A partir de este % de la capacidad de espacios, el modelo puede ser factible pero
        /// agotar el timeout por saturación (muchas sesiones compitiendo por pocos huecos).
        /// Solo dispara un LogWarning — no cambia el rechazo por demanda &gt; capacidad.
        /// </summary>
        private const decimal UmbralSaturacion = 0.95m;

        public MotorConstraintProgramming(ILogger<MotorConstraintProgramming> logger, CpSatOptions? options = null)
        {
            _logger = logger;
            _options = options ?? new CpSatOptions();
        }

        public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes,
            IEnumerable<Grupo>? grupos = null,
            IEnumerable<Guid>? sesionesFijasIds = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            CancellationToken ct = default)
        {
            var s     = sesiones.ToList();
            var b     = bloques.ToList();
            var e     = espacios.ToList();
            var d     = docentes.ToList();
            var g     = grupos?.ToList() ?? new List<Grupo>();
            var fijas = sesionesFijasIds != null
                ? new HashSet<Guid>(sesionesFijasIds)
                : new HashSet<Guid>();
            var ventanas = ventanaPorAsignatura ?? new Dictionary<Guid, (TimeOnly?, TimeOnly?)>();
            return Task.Run(() => ResolverSincrono(s, b, e, d, g, fijas, ventanas, ct), ct);
        }

        /// <summary>
        /// Modalidad derivada para una semana concreta. Dato fijo, no lo decide el solver.
        /// Una sesión marcada como Virtual (asignatura totalmente en línea) es virtual en ambas
        /// semanas; solo las sesiones presenciales alternan según TipoA/TipoB (regla 9).
        /// </summary>
        private static Modalidad ModalidadDe(Sesion sesion, SemanaAcademica semana)
            => ModalidadSemanal.Derivar(sesion, semana); // fuente única de la regla 9 (Domain)

        private static IReadOnlyList<AsignacionSemanal> SinAsignaciones => Array.Empty<AsignacionSemanal>();

        private ResultadoFactibilidad ResolverSincrono(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Espacio> espacios,
            List<Docente> docentes,
            List<Grupo> grupos,
            HashSet<Guid> sesionesFijasIds,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)> ventanaPorAsignatura,
            CancellationToken ct)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques para procesar en la Fase 2.");
                return new ResultadoFactibilidad(false, SinAsignaciones, "No hay datos suficientes.");
            }

            // ── Capacidad de espacios vs demanda presencial POR SEMANA (virtuales no ocupan espacio) ──
            int capacidadBloquesHora = espacios.Count * bloques.Count;
            foreach (var semana in Semanas)
            {
                decimal demandaPresencial = sesiones
                    .Where(s => ModalidadDe(s, semana) == Modalidad.Presencial)
                    .Sum(s => s.DuracionHoras);

                _logger.LogInformation(
                    "Fase 2 (CP-SAT) Semana {W}: demanda presencial={Dem}h vs capacidad={Cap}h ({E} espacios × {B} bloques).",
                    semana, demandaPresencial, capacidadBloquesHora, espacios.Count, bloques.Count);

                if (capacidadBloquesHora > 0 && demandaPresencial / capacidadBloquesHora >= UmbralSaturacion)
                {
                    _logger.LogWarning(
                        "Fase 2 (CP-SAT) Semana {W}: demanda al {Pct:P0} de la capacidad de espacios — " +
                        "el modelo es factible en teoría pero puede saturarse y agotar el timeout. " +
                        "Considere más espacios o revisar la distribución de alternancia.",
                        semana, demandaPresencial / capacidadBloquesHora);
                }

                if (demandaPresencial > capacidadBloquesHora)
                {
                    var msg = $"Infactible (Semana {semana}): la demanda presencial ({demandaPresencial}h) supera la capacidad de espacios " +
                              $"({capacidadBloquesHora}h = {espacios.Count} espacio(s) × {bloques.Count} bloques). " +
                              "Añada más espacios o ajuste la alternancia.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                }
            }

            var model = new CpModel();

            // ── Índices y helpers ───────────────────────────────────────────────────────
            var bloqueIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;

            var espacioIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < espacios.Count; i++) espacioIndex[espacios[i].Id] = i;

            var rangosPorDia = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // HC-G01: índice de disponibilidad por GrupoId.
            // Si el grupo declara Disponibilidad (Matutino/Vespertino), los starts de sus sesiones
            // quedan confinados a los bloques que caen dentro de esa franja (hard constraint).
            // Matutino = HoraInicio < 12:00 | Vespertino = HoraInicio >= 12:00.
            var bloquesPermitidosPorGrupo = new Dictionary<Guid, HashSet<int>>();
            foreach (var grupo in grupos)
            {
                if (grupo.Id == Guid.Empty || grupo.Disponibilidad.Count == 0) continue;
                var permiteMatutino   = grupo.Disponibilidad.Contains(FranjaHoraria.Matutino);
                var permiteVespertino = grupo.Disponibilidad.Contains(FranjaHoraria.Vespertino);
                var permitidos = new HashSet<int>();
                for (int i = 0; i < bloques.Count; i++)
                {
                    var hora = bloques[i].HoraInicio;
                    if (permiteMatutino   && hora.Hour < 12) permitidos.Add(i);
                    if (permiteVespertino && hora.Hour >= 12) permitidos.Add(i);
                }
                if (permitidos.Count > 0)
                    bloquesPermitidosPorGrupo[grupo.Id] = permitidos;
            }

            // HC-CAP: estudiantes por grupo. Un espacio solo es candidato si su aforo alcanza
            // para los estudiantes del grupo de la sesión (Espacio.Capacidad >= EstudiantesInscritos).
            var estudiantesPorGrupo = grupos
                .Where(gr => gr.Id != Guid.Empty)
                .GroupBy(gr => gr.Id)
                .ToDictionary(gr => gr.Key, gr => gr.First().EstudiantesInscritos);

            // ── Variables: intervalo de longitud DuracionHoras por (sesión, semana) ─────
            var startVars    = new Dictionary<(Guid, SemanaAcademica), IntVar>();
            var endVars      = new Dictionary<(Guid, SemanaAcademica), IntVar>();
            var intervalVars = new Dictionary<(Guid, SemanaAcademica), IntervalVar>();
            var spaceVars    = new Dictionary<(Guid, SemanaAcademica), IntVar>(); // solo pares presenciales
            var duraciones   = new Dictionary<Guid, int>();

            foreach (var sesion in sesiones)
            {
                int duracion = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));
                duraciones[sesion.Id] = duracion;

                foreach (var semana in Semanas)
                {
                    var key = (sesion.Id, semana);
                    var startVar = model.NewIntVar(0, bloques.Count - duracion, $"start_{sesion.Id}_{semana}");
                    var endVar   = model.NewIntVar(duracion, bloques.Count, $"end_{sesion.Id}_{semana}");
                    startVars[key] = startVar;
                    endVars[key]   = endVar;
                    intervalVars[key] = model.NewIntervalVar(startVar, duracion, endVar, $"int_{sesion.Id}_{semana}");

                    if (espacios.Any() && ModalidadDe(sesion, semana) == Modalidad.Presencial)
                        spaceVars[key] = model.NewIntVar(0, espacios.Count - 1, $"space_{sesion.Id}_{semana}");
                }
            }

            // ── Enlace regla 9: para alternancia, la franja virtual = franja presencial ─
            foreach (var sesion in sesiones)
            {
                if (sesion.Alternancia is TipoAlternancia.TipoA or TipoAlternancia.TipoB)
                    model.Add(startVars[(sesion.Id, SemanaAcademica.A)] == startVars[(sesion.Id, SemanaAcademica.B)]);
            }

            // ── Warm-start y fijación de sesiones base ───────────────────────────────
            foreach (var sesion in sesiones)
            {
                if (sesion.Estado != EstadoSesion.Asignada) continue;
                if (sesion.BloqueTiempoId == Guid.Empty) continue;
                if (!bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx)) continue;

                int dur = duraciones[sesion.Id];
                if (!BloquesPlanner.CabeEnDia(idx, dur, rangosPorDia, diaPorIdx)) continue;

                if (sesionesFijasIds.Contains(sesion.Id))
                {
                    // Sesión del horario base: igualdad estricta — CP-SAT no puede moverla.
                    foreach (var semana in Semanas)
                        model.Add(startVars[(sesion.Id, semana)] == idx);
                }
                else
                {
                    // Fase 1 warm-start: pista, el solver puede sobreescribirla.
                    foreach (var semana in Semanas)
                    {
                        model.AddHint(startVars[(sesion.Id, semana)], idx);

                        // Si Fase 1 ya trae espacio asignado, hintear también spaceVars:
                        // si esa asignación es factible, CP-SAT la valida casi al instante.
                        if (sesion.EspacioId is Guid espacioHint &&
                            espacioIndex.TryGetValue(espacioHint, out var espIdx) &&
                            spaceVars.TryGetValue((sesion.Id, semana), out var spaceVar))
                            model.AddHint(spaceVar, espIdx);
                    }
                }
            }

            // ── HC-G01 + "No cruzar día": dominio de start por sesión y semana ────────────
            // HC-G01 (presencial-first): si el grupo del grupo declara Disponibilidad, los starts
            // quedan confinados a los bloques de esa franja (hard constraint).
            // La disponibilidad docente YA NO restringe (CR-08): bloquesDisponibles para el
            // filtro de docente siempre es null. Las sesiones fijas ya tienen igualdad — se omiten.
            foreach (var sesion in sesiones)
            {
                if (sesionesFijasIds.Contains(sesion.Id)) continue;

                int dur = duraciones[sesion.Id];

                // Obtener restricción de disponibilidad del grupo (HC-G01)
                HashSet<int>? permitidosPorGrupo = null;
                if (sesion.GrupoId.HasValue &&
                    bloquesPermitidosPorGrupo.TryGetValue(sesion.GrupoId.Value, out var perm))
                    permitidosPorGrupo = perm;

                var todosStarts = BloquesPlanner
                    .StartsValidos(dur, bloques.Count, rangosPorDia, diaPorIdx, bloquesDisponibles: null);

                // Aplicar filtro HC-G01 si el grupo tiene disponibilidad declarada
                var startsValidos = (permitidosPorGrupo is not null)
                    ? todosStarts.Where(s => permitidosPorGrupo.Contains(s)).Select(v => (long)v).ToArray()
                    : todosStarts.Select(v => (long)v).ToArray();

                if (startsValidos.Length == 0)
                {
                    var msg = permitidosPorGrupo is not null
                        ? $"HC-G01: no hay bloques válidos para la sesión de {dur}h dentro de la disponibilidad declarada del grupo (GrupoId={sesion.GrupoId})."
                        : $"No hay bloques válidos para una sesión de {dur}h (no cabe sin cruzar día en la grilla canónica).";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                }

                // HC-VH: ventana horaria de la asignatura (hard). El intervalo completo
                // [inicio, inicio+dur] debe caer dentro de [min, max]. Como StartsValidos ya
                // garantiza que la sesión no cruza día, inicio+dur nunca se desborda al día siguiente.
                if (ventanaPorAsignatura.TryGetValue(sesion.AsignaturaId, out var ventana) &&
                    (ventana.min.HasValue || ventana.max.HasValue))
                {
                    startsValidos = startsValidos.Where(s =>
                    {
                        var inicio = bloques[(int)s].HoraInicio;
                        var fin    = inicio.AddHours(dur);
                        if (ventana.min.HasValue && inicio < ventana.min.Value) return false;
                        if (ventana.max.HasValue && fin    > ventana.max.Value) return false;
                        return true;
                    }).ToArray();

                    if (startsValidos.Length == 0)
                    {
                        var msg = $"HC-VH infactible: la sesión de {dur}h de la asignatura {sesion.AsignaturaId} no " +
                                  $"cabe dentro de su ventana horaria [{ventana.min:HH\\:mm}–{ventana.max:HH\\:mm}] " +
                                  "en ningún día de la grilla. Amplíe la ventana o reduzca la duración.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                    }
                }

                var dominio = CpDomain.FromValues(startsValidos);
                foreach (var semana in Semanas)
                    model.AddLinearExpressionInDomain(startVars[(sesion.Id, semana)], dominio);
            }

            // ── HC-C01: conflicto de cohorte — NoOverlap por (grupo, semana) ──────────
            // CR-08 (presencial-first): el grupo de estudiantes es el eje de no-solapamiento (el
            // docente sale del pipeline y se asigna después de generar). Incluye presenciales y
            // virtuales: ambos consumen el tiempo del grupo. Con cohorte única por run ⇒ un
            // NoOverlap global por semana (todas las sesiones se serializan). No hay equivalente
            // de "máx. horas" para el grupo, así que HC-I03 (carga docente) desaparece.
            var sesionesPorGrupo = sesiones.GroupBy(s => s.GrupoId).Where(g => g.Key.HasValue).ToList();
            foreach (var grupo in sesionesPorGrupo)
            {
                foreach (var semana in Semanas)
                {
                    var intervals = grupo.Select(s => intervalVars[(s.Id, semana)]).ToArray();
                    if (intervals.Length > 1)
                        model.AddNoOverlap(intervals);
                }
            }

            // ── HC-S01 + HC-S03 + HC-S04: espacio por (espacio, semana), solo presencial ─
            if (spaceVars.Any() && espacios.Any())
            {
                // HC-S03 + HC-S05: candidatos por sesión.
                // HC-S05: si la sesión trae un EspacioId específico y ese espacio existe en la
                // lista, solo ese espacio es candidato (respeta la asignación del curriculum).
                // HC-S03: si no hay espacio fijo pero requiere laboratorio, se filtran por tipo.
                var candidatosPorSesion = new Dictionary<Guid, List<int>>();
                foreach (var sesion in sesiones)
                {
                    bool tienePresencial = Semanas.Any(w => spaceVars.ContainsKey((sesion.Id, w)));
                    if (!tienePresencial) continue;

                    List<int> lista;

                    // HC-S05: espacio fijo → solo ese índice
                    if (sesion.EspacioId.HasValue && espacioIndex.TryGetValue(sesion.EspacioId.Value, out var idxFijo))
                    {
                        lista = new List<int> { idxFijo };
                    }
                    else
                    {
                        // HC-S03: sin espacio fijo → filtrar por tipo de espacio
                        bool requiereLab = sesion.EspacioId.HasValue &&
                            espacios.FirstOrDefault(e => e.Id == sesion.EspacioId)?.Tipo == TipoEspacio.Laboratorio;

                        lista = new List<int>();
                        for (int e = 0; e < espacios.Count; e++)
                        {
                            if (requiereLab && espacios[e].Tipo != TipoEspacio.Laboratorio) continue;
                            lista.Add(e);
                        }
                    }

                    if (lista.Count == 0)
                    {
                        var msg = $"Sesión {sesion.Id} requiere laboratorio pero no hay espacios de ese tipo configurados.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                    }

                    // HC-CAP: descartar espacios con aforo insuficiente para el grupo de la sesión.
                    int estudiantes = sesion.GrupoId.HasValue &&
                        estudiantesPorGrupo.TryGetValue(sesion.GrupoId.Value, out var nEst) ? nEst : 0;
                    if (estudiantes > 0)
                    {
                        var conAforo = lista.Where(e => espacios[e].Capacidad >= estudiantes).ToList();
                        if (conAforo.Count == 0)
                        {
                            int aforoMax = lista.Max(e => espacios[e].Capacidad);
                            var msg = $"HC-CAP infactible: la sesión {sesion.Id} necesita un espacio para {estudiantes} " +
                                      $"estudiantes, pero el aforo máximo disponible entre sus candidatos es {aforoMax}. " +
                                      "Añada un espacio con mayor capacidad o reduzca el grupo.";
                            _logger.LogError(msg);
                            return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                        }
                        lista = conAforo;
                    }

                    candidatosPorSesion[sesion.Id] = lista;
                }

                // Intervalos opcionales agrupados por (espacio, semana).
                var optIntervalsPorEspacioSemana = new Dictionary<(int espacio, SemanaAcademica semana), List<IntervalVar>>();
                for (int e = 0; e < espacios.Count; e++)
                    foreach (var semana in Semanas)
                        optIntervalsPorEspacioSemana[(e, semana)] = new List<IntervalVar>();

                foreach (var key in spaceVars.Keys)
                {
                    var (sesionId, semana) = key;
                    if (!candidatosPorSesion.TryGetValue(sesionId, out var candidatos)) continue;

                    var literales = new List<ILiteral>();
                    foreach (var e in candidatos)
                    {
                        var lit = model.NewBoolVar($"sel_{sesionId}_{semana}_{e}");
                        literales.Add(lit);

                        model.Add(spaceVars[key] == e).OnlyEnforceIf(lit);

                        var optInt = model.NewOptionalIntervalVar(
                            startVars[key],
                            duraciones[sesionId],
                            endVars[key],
                            lit,
                            $"optInt_{sesionId}_{semana}_{e}");
                        optIntervalsPorEspacioSemana[(e, semana)].Add(optInt);
                    }

                    model.AddExactlyOne(literales);
                }

                // NoOverlap por cada (espacio físico, semana): un slot puede reusarse en semanas distintas.
                foreach (var par in optIntervalsPorEspacioSemana)
                {
                    if (par.Value.Count > 1)
                        model.AddNoOverlap(par.Value);
                }
            }

            // ── RESOLVER ────────────────────────────────────────────────────────────────
            // El volcado a disco solo ocurre si se habilita explícitamente (P0.2 auditoría).
            if (_options.ExportarModelo)
            {
                try
                {
                    System.IO.File.WriteAllText("cp_model_debug.txt", model.Model.ToString());
                    _logger.LogInformation("Modelo CP-SAT exportado a cp_model_debug.txt para inspección.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo exportar el modelo CP-SAT.");
                }
            }

            var solver = new CpSolver();
            solver.StringParameters =
                $"max_time_in_seconds:{_options.TimeoutSegundos},log_search_progress:true" +
                (_options.NumWorkers > 0 ? $",num_search_workers:{_options.NumWorkers}" : "");

            _logger.LogInformation("Resolviendo modelo CP-SAT (timeout: {T}s)...", _options.TimeoutSegundos);
            using var reg = ct.Register(() => solver.StopSearch());
            var status = solver.Solve(model);
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("CP-SAT terminó con status: {Status}", status);

            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                var asignaciones = new List<AsignacionSemanal>(sesiones.Count * 2);
                foreach (var sesion in sesiones)
                {
                    foreach (var semana in Semanas)
                    {
                        var key = (sesion.Id, semana);
                        var startIdx = (int)solver.Value(startVars[key]);
                        var bloqueAsignado = bloques[startIdx];

                        var modalidad = ModalidadDe(sesion, semana);
                        Guid? espacioAsignado = null;
                        if (spaceVars.TryGetValue(key, out var spaceVar))
                        {
                            var espacioIdx = (int)solver.Value(spaceVar);
                            if (espacioIdx >= 0 && espacioIdx < espacios.Count)
                                espacioAsignado = espacios[espacioIdx].Id;
                        }

                        asignaciones.Add(new AsignacionSemanal(
                            Guid.NewGuid(),
                            sesion.Id,
                            semana,
                            bloqueAsignado.Id,
                            espacioAsignado,
                            modalidad));
                    }
                }

                _logger.LogInformation(
                    "Fase 2 completada exitosamente. {N} asignaciones semanales (2 por sesión) factibles.",
                    asignaciones.Count);
                return new ResultadoFactibilidad(true, asignaciones.AsReadOnly(), "");
            }

            _logger.LogWarning("CP-SAT no encontró solución factible. Status: {Status}", status);
            return new ResultadoFactibilidad(false, SinAsignaciones,
                $"No se encontró solución factible. Status del solver: {status}. Revise restricciones de docente, espacio y disponibilidad.");
        }
    }
}
