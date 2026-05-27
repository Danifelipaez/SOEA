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
    /// Modela cada sesión como un IntervalVar de longitud fija = DuracionHoras
    /// e impone NoOverlap por docente y por espacio.
    /// La duración es un dato de entrada inmutable (CLAUDE.md regla 6).
    /// </summary>
    public class MotorConstraintProgramming : IMotorConstraintProgramming
    {
        private readonly ILogger<MotorConstraintProgramming> _logger;
        private const int TimeoutSegundos = 120;

        public MotorConstraintProgramming(ILogger<MotorConstraintProgramming> logger)
        {
            _logger = logger;
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

        private ResultadoFactibilidad ResolverSincrono(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Espacio> espacios,
            List<Docente> docentes)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques para procesar en la Fase 2.");
                return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), "No hay datos suficientes.");
            }

            // ── Capacidad de espacios vs demanda presencial (virtuales no ocupan espacio físico) ──
            int capacidadBloquesHora    = espacios.Count * bloques.Count;
            var sesionesPresenciales    = sesiones.Where(s => s.Modalidad != Modalidad.Virtual).ToList();
            decimal demandaPresencial   = sesionesPresenciales.Sum(s => s.DuracionHoras);
            decimal demandaTotal        = sesiones.Sum(s => s.DuracionHoras);

            _logger.LogInformation(
                "Fase 2 (CP-SAT): {S} sesiones ({SP} presenciales / {SV} virtuales), {B} bloques, {E} espacios, {D} docentes. " +
                "Demanda presencial={Dem}h vs Capacidad={Cap}h.",
                sesiones.Count, sesionesPresenciales.Count, sesiones.Count - sesionesPresenciales.Count,
                bloques.Count, espacios.Count, docentes.Count,
                demandaPresencial, capacidadBloquesHora);

            if (demandaPresencial > capacidadBloquesHora)
            {
                var msg = $"Infactible: la demanda presencial ({demandaPresencial}h) supera la capacidad de espacios " +
                          $"({capacidadBloquesHora}h = {espacios.Count} espacio(s) × {bloques.Count} bloques). " +
                          "Añada más espacios o marque sesiones como virtuales.";
                _logger.LogError(msg);
                return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
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

            // ── Variables: intervalo de longitud DuracionHoras por sesión ───────────────
            var startVars    = new Dictionary<Guid, IntVar>();
            var endVars      = new Dictionary<Guid, IntVar>();
            var intervalVars = new Dictionary<Guid, IntervalVar>();
            var spaceVars    = new Dictionary<Guid, IntVar>();
            var duraciones   = new Dictionary<Guid, int>();

            foreach (var sesion in sesiones)
            {
                int duracion = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));
                duraciones[sesion.Id] = duracion;

                var startVar = model.NewIntVar(0, bloques.Count - duracion, $"start_{sesion.Id}");
                var endVar   = model.NewIntVar(duracion, bloques.Count, $"end_{sesion.Id}");
                startVars[sesion.Id] = startVar;
                endVars[sesion.Id]   = endVar;
                intervalVars[sesion.Id] = model.NewIntervalVar(startVar, duracion, endVar, $"int_{sesion.Id}");

                if (espacios.Any() && sesion.Modalidad != Modalidad.Virtual)
                    spaceVars[sesion.Id] = model.NewIntVar(0, espacios.Count - 1, $"space_{sesion.Id}");
            }

            // ── Warm-start desde Fase 1 (GraphColoring asigna bloques, no espacios) ────
            foreach (var sesion in sesiones)
            {
                if (sesion.Estado == EstadoSesion.Asignada && sesion.BloqueTiempoId != Guid.Empty &&
                    bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx))
                {
                    int dur = duraciones[sesion.Id];
                    if (BloquesPlanner.CabeEnDia(idx, dur, rangosPorDia, diaPorIdx))
                        model.AddHint(startVars[sesion.Id], idx);
                }
            }

            // ── HC-I02 + "no cruzar día": dominio de start filtrado por sesión ─────────
            // El docente debe tener TODOS los bloques cubiertos por el span [start, start+dur)
            // disponibles, y el span no debe cruzar al día siguiente.
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
                        _logger.LogWarning(
                            "HC-I02: Docente {D} no tiene bloques disponibles que coincidan con la grilla canónica.",
                            docente.NombreCompleto);
                        bloquesDocente = null;
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
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }

                model.AddLinearExpressionInDomain(startVars[sesion.Id], CpDomain.FromValues(startsValidos));
            }

            // ── HC-I03: pre-solve de carga semanal y capacidad por docente ─────────────
            // Materializar aquí para que HC-I01 reutilice la misma lista sin re-evaluar el GroupBy.
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
                    var msg = $"Docente {docente.NombreCompleto} tiene {totalHoras}h asignadas, excede su máximo de {docente.MaximoHorasSemanales}h semanales.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }

                // Pre-check de capacidad: la suma de horas no cabe en los bloques disponibles del docente.
                var bloquesValidos = docente.BloquesDisponibles.Count(b => bloqueIndex.ContainsKey(b.Id));
                if (totalHoras > bloquesValidos)
                {
                    var msg = $"Docente {docente.NombreCompleto} requiere {totalHoras}h pero solo {bloquesValidos} bloques de 1h coinciden con la grilla canónica — imposible agendar sin solapamientos.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }
            }

            // ── HC-I01: conflicto de docente — NoOverlap sobre intervalos del docente ──
            foreach (var grupo in sesionesPorDocente)
            {
                var intervals = grupo.Select(s => intervalVars[s.Id]).ToArray();
                if (intervals.Length > 1)
                    model.AddNoOverlap(intervals);
            }

            // ── HC-S01 + HC-S03: espacio (intervalos opcionales por sesión×espacio) ───
            if (spaceVars.Any() && espacios.Any())
            {
                // Para cada sesión presencial, candidatos = espacios cuyo tipo respeta HC-S03
                var candidatosPorSesion = new Dictionary<Guid, List<int>>();
                foreach (var sesion in sesiones)
                {
                    if (!spaceVars.ContainsKey(sesion.Id)) continue;

                    // HC-S03: si la sesión venía con un espacio de tipo Laboratorio, restringir a laboratorios.
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
                        // Devolvemos infactible sin construir el resto del modelo
                        return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                    }
                    candidatosPorSesion[sesion.Id] = lista;
                }

                // Literales y intervalos opcionales: lit_{s,e} = "sesión s en espacio e"
                var litPorSesionEspacio = new Dictionary<(Guid sesion, int espacio), BoolVar>();
                var optIntervalsPorEspacio = new Dictionary<int, List<IntervalVar>>();
                for (int e = 0; e < espacios.Count; e++) optIntervalsPorEspacio[e] = new List<IntervalVar>();

                foreach (var sesion in sesiones)
                {
                    if (!candidatosPorSesion.TryGetValue(sesion.Id, out var candidatos)) continue;

                    var literales = new List<ILiteral>();
                    foreach (var e in candidatos)
                    {
                        var lit = model.NewBoolVar($"sel_{sesion.Id}_{e}");
                        litPorSesionEspacio[(sesion.Id, e)] = lit;
                        literales.Add(lit);

                        // Vincular literal → spaceVar (solo cuando está activo; AddExactlyOne
                        // garantiza exclusividad, no se necesita el sentido negado "!= e").
                        model.Add(spaceVars[sesion.Id] == e).OnlyEnforceIf(lit);

                        // Intervalo opcional para NoOverlap por espacio
                        var optInt = model.NewOptionalIntervalVar(
                            startVars[sesion.Id],
                            duraciones[sesion.Id],
                            endVars[sesion.Id],
                            lit,
                            $"optInt_{sesion.Id}_{e}");
                        optIntervalsPorEspacio[e].Add(optInt);
                    }

                    // Exactamente uno de los espacios candidatos debe estar activo
                    model.AddExactlyOne(literales);
                }

                // NoOverlap por cada espacio físico
                foreach (var par in optIntervalsPorEspacio)
                {
                    if (par.Value.Count > 1)
                        model.AddNoOverlap(par.Value);
                }
            }

            // ── RESOLVER ────────────────────────────────────────────────────────────────
            try
            {
                System.IO.File.WriteAllText("cp_model_debug.txt", model.Model.ToString());
                _logger.LogInformation("Modelo CP-SAT exportado a cp_model_debug.txt para inspección.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo exportar el modelo CP-SAT.");
            }

            var solver = new CpSolver();
            solver.StringParameters = $"max_time_in_seconds:{TimeoutSegundos},log_search_progress:true";

            _logger.LogInformation("Resolviendo modelo CP-SAT (timeout: {T}s)...", TimeoutSegundos);
            var status = solver.Solve(model);
            _logger.LogInformation("CP-SAT terminó con status: {Status}", status);

            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                foreach (var sesion in sesiones)
                {
                    var startIdx = (int)solver.Value(startVars[sesion.Id]);
                    var bloqueAsignado = bloques[startIdx];
                    sesion.AsignarBloqueTiempo(bloqueAsignado.Id);

                    if (spaceVars.ContainsKey(sesion.Id))
                    {
                        var espacioIdx = (int)solver.Value(spaceVars[sesion.Id]);
                        if (espacioIdx >= 0 && espacioIdx < espacios.Count)
                            sesion.AsignarEspacio(espacios[espacioIdx].Id);
                    }
                }

                _logger.LogInformation("Fase 2 completada exitosamente. Todas las sesiones tienen asignación factible.");
                return new ResultadoFactibilidad(true, sesiones.AsReadOnly(), "");
            }

            _logger.LogWarning("CP-SAT no encontró solución factible. Status: {Status}", status);
            return new ResultadoFactibilidad(false, sesiones.AsReadOnly(),
                $"No se encontró solución factible. Status del solver: {status}. Revise restricciones de docente, espacio y disponibilidad.");
        }
    }
}
