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

        public MotorConstraintProgramming(ILogger<MotorConstraintProgramming> logger, CpSatOptions? options = null)
        {
            _logger = logger;
            _options = options ?? new CpSatOptions();
        }

        public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes)
        {
            var resultado = ResolverSincrono(sesiones.ToList(), bloques.ToList(), espacios.ToList(), docentes.ToList());
            return Task.FromResult(resultado);
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
            List<Docente> docentes)
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

            var docenteDict = docentes.ToDictionary(d => d.Id);
            var rangosPorDia = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

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

            // ── Warm-start desde Fase 1 (GraphColoring asigna bloques, no espacios) ────
            foreach (var sesion in sesiones)
            {
                if (sesion.Estado == EstadoSesion.Asignada && sesion.BloqueTiempoId != Guid.Empty &&
                    bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx))
                {
                    int dur = duraciones[sesion.Id];
                    if (BloquesPlanner.CabeEnDia(idx, dur, rangosPorDia, diaPorIdx))
                    {
                        foreach (var semana in Semanas)
                            model.AddHint(startVars[(sesion.Id, semana)], idx);
                    }
                }
            }

            // ── HC-I02 + "no cruzar día": dominio de start filtrado por sesión y semana ─
            foreach (var sesion in sesiones)
            {
                int dur = duraciones[sesion.Id];
                ISet<int>? bloquesDocente = null;

                if (docenteDict.TryGetValue(sesion.DocenteId, out var docente))
                {
                    bloquesDocente = docente.BloquesDisponibles
                        .Where(b => bloqueIndex.ContainsKey(b.Id))
                        .Select(b => bloqueIndex[b.Id])
                        .ToHashSet();

                    if (bloquesDocente.Count == 0)
                    {
                        // HC-I02 (P1.3 auditoría): un docente sin bloques disponibles que calcen con
                        // la grilla está explícitamente NO disponible. No se trata como "sin
                        // restricción" (lo que permitiría agendarlo en cualquier bloque): se rechaza
                        // con un mensaje claro, en vez de agendar silenciosamente a quien el usuario
                        // marcó como no disponible.
                        var msg = $"Docente {docente.NombreCompleto} no tiene disponibilidad configurada que coincida con la grilla; no se pueden agendar sus sesiones.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                    }
                }

                var startsValidos = BloquesPlanner
                    .StartsValidos(dur, bloques.Count, rangosPorDia, diaPorIdx, bloquesDocente)
                    .Select(v => (long)v)
                    .ToArray();

                if (startsValidos.Length == 0)
                {
                    var nombre = docenteDict.TryGetValue(sesion.DocenteId, out var d) ? d.NombreCompleto : sesion.DocenteId.ToString();
                    var msg = $"No hay bloques válidos para una sesión de {dur}h del docente {nombre} (disponibilidad insuficiente o no cabe sin cruzar día).";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                }

                var dominio = CpDomain.FromValues(startsValidos);
                foreach (var semana in Semanas)
                    model.AddLinearExpressionInDomain(startVars[(sesion.Id, semana)], dominio);
            }

            // ── HC-I03: pre-solve de carga semanal y capacidad por docente ─────────────
            // Cada sesión ocupa tiempo de docente en AMBAS semanas (la virtual es sincrónica),
            // por lo que la carga por semana = suma de duraciones de sus sesiones.
            var sesionesPorDocente = sesiones.GroupBy(s => s.DocenteId).Where(g => g.Key != Guid.Empty).ToList();
            foreach (var grupo in sesionesPorDocente)
            {
                if (!docenteDict.TryGetValue(grupo.Key, out var docente)) continue;

                if (docente.BloquesDisponibles.Count == 0)
                {
                    _logger.LogWarning(
                        "Docente {D} no tiene disponibilidad definida — sus sesiones usarán cualquier bloque de la grilla.",
                        docente.NombreCompleto);
                    continue;
                }

                var totalHoras = grupo.Sum(s => s.DuracionHoras);
                if (totalHoras > docente.MaximoHorasSemanales)
                {
                    var msg = $"Docente {docente.NombreCompleto} tiene {totalHoras}h asignadas por semana, excede su máximo de {docente.MaximoHorasSemanales}h semanales.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                }

                var bloquesValidos = docente.BloquesDisponibles.Count(b => bloqueIndex.ContainsKey(b.Id));
                if (totalHoras > bloquesValidos)
                {
                    var msg = $"Docente {docente.NombreCompleto} requiere {totalHoras}h pero solo {bloquesValidos} bloques de 1h coinciden con la grilla canónica — imposible agendar sin solapamientos.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg);
                }
            }

            // ── HC-I01: conflicto de docente — NoOverlap por (docente, semana) ─────────
            // Incluye presenciales y virtuales: ambos consumen tiempo del docente.
            foreach (var grupo in sesionesPorDocente)
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
                // HC-S03: candidatos por sesión (mismo requerimiento de lab en ambas semanas).
                var candidatosPorSesion = new Dictionary<Guid, List<int>>();
                foreach (var sesion in sesiones)
                {
                    bool tienePresencial = Semanas.Any(w => spaceVars.ContainsKey((sesion.Id, w)));
                    if (!tienePresencial) continue;

                    bool requiereLab = false;
                    if (sesion.EspacioId.HasValue)
                    {
                        var espOriginal = espacios.FirstOrDefault(e => e.Id == sesion.EspacioId);
                        if (espOriginal != null && espOriginal.Tipo == TipoEspacio.Laboratorio)
                            requiereLab = true;
                    }

                    var lista = new List<int>();
                    for (int e = 0; e < espacios.Count; e++)
                    {
                        if (requiereLab && espacios[e].Tipo != TipoEspacio.Laboratorio) continue;
                        lista.Add(e);
                    }

                    if (lista.Count == 0)
                    {
                        var msg = $"Sesión {sesion.Id} requiere laboratorio pero no hay espacios de ese tipo configurados.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg);
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
            solver.StringParameters = $"max_time_in_seconds:{_options.TimeoutSegundos},log_search_progress:true";

            _logger.LogInformation("Resolviendo modelo CP-SAT (timeout: {T}s)...", _options.TimeoutSegundos);
            var status = solver.Solve(model);
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
