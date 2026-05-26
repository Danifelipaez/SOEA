using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using CpDomain = Google.OrTools.Util.Domain;

namespace SOEA.Engine.ConstraintProg
{
    /// <summary>
    /// Motor de Constraint Programming (Fase 2) usando Google OR-Tools CP-SAT.
    /// Impone todas las restricciones duras del piloto y encuentra un horario factible.
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

            _logger.LogInformation("Fase 2 (CP-SAT): Construyendo modelo con {S} sesiones, {B} bloques, {E} espacios, {D} docentes.",
                sesiones.Count, bloques.Count, espacios.Count, docentes.Count);

            int capacidadTotal = espacios.Count * bloques.Count;
            _logger.LogInformation("Fase 2 (CP-SAT) Debug: Capacidad teórica calculada = {E} espacios x {B} bloques = {C} slots.", 
                espacios.Count, bloques.Count, capacidadTotal);
            if (sesiones.Count > capacidadTotal)
            {
                _logger.LogWarning("¡ALERTA MATEMÁTICA! El número de sesiones ({S}) supera la capacidad total ({C}) dada por los bloques y espacios disponibles. El modelo será lógicamente infactible bajo la regla HC-S01.", 
                    sesiones.Count, capacidadTotal);
            }

            var model = new CpModel();

            // --- Índices ---
            var bloqueIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;

            var espacioIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < espacios.Count; i++) espacioIndex[espacios[i].Id] = i;

            var docenteDict = docentes.ToDictionary(d => d.Id);

            // --- Variables de decisión ---
            var timeVars = new Dictionary<Guid, IntVar>();
            var spaceVars = new Dictionary<Guid, IntVar>();

            foreach (var sesion in sesiones)
            {
                timeVars[sesion.Id] = model.NewIntVar(0, bloques.Count - 1, $"time_{sesion.Id}");

                // Si la sesión requiere espacio físico, creamos variable
                if (espacios.Any() && sesion.Modalidad != Modalidad.Virtual)
                    spaceVars[sesion.Id] = model.NewIntVar(0, espacios.Count - 1, $"space_{sesion.Id}");
            }

            // --- Warm Start (indicios de la Fase 1) ---
            foreach (var sesion in sesiones)
            {
                if (sesion.Estado == EstadoSesion.Asignada && sesion.BloqueTiempoId != Guid.Empty)
                {
                    if (bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx))
                    {
                        model.AddHint(timeVars[sesion.Id], idx);
                    }
                }
                if (sesion.EspacioId.HasValue && sesion.EspacioId != Guid.Empty && espacioIndex.TryGetValue(sesion.EspacioId.Value, out var sIdx))
                {
                    if (spaceVars.ContainsKey(sesion.Id))
                        model.AddHint(spaceVars[sesion.Id], sIdx);
                }
            }

            // --- PRE-RESTRICCIÓN: dominio de bloque restringido a slots que caben en el mismo día ---
            // Calcula, para cada índice de bloque, el índice exclusivo del fin de su día.
            var dayEndIdx = new int[bloques.Count];
            {
                DiaDeSemana? prevDia = null;
                int dayStart = 0;
                for (int b = 0; b < bloques.Count; b++)
                {
                    if (prevDia.HasValue && bloques[b].Dia != prevDia.Value)
                    {
                        for (int k = dayStart; k < b; k++) dayEndIdx[k] = b;
                        dayStart = b;
                    }
                    prevDia = bloques[b].Dia;
                }
                for (int k = dayStart; k < bloques.Count; k++) dayEndIdx[k] = bloques.Count;
            }

            // Para sesiones con duración > 1h, restringir el inicio a bloques donde cabe toda la duración.
            foreach (var s in sesiones)
            {
                int d = (int)Math.Ceiling(s.DuracionHoras);
                if (d <= 1 || !timeVars.ContainsKey(s.Id)) continue;

                var validIdx = Enumerable.Range(0, bloques.Count)
                    .Where(b => b + d <= dayEndIdx[b])
                    .Select(b => (long)b)
                    .ToArray();

                if (validIdx.Length > 0)
                    model.AddLinearExpressionInDomain(timeVars[s.Id], CpDomain.FromValues(validIdx));
            }

            // --- RESTRICCIONES DURAS ---

            // HC-I01: Conflicto de Docente — ningún docente puede tener sesiones que se solapen en el tiempo
            var sesionesPorDocente = sesiones.GroupBy(s => s.DocenteId).Where(g => g.Key != Guid.Empty);
            foreach (var grupo in sesionesPorDocente)
            {
                var sesionesDelDocente = grupo.ToList();
                if (sesionesDelDocente.Count <= 1) continue;

                for (int pi = 0; pi < sesionesDelDocente.Count; pi++)
                {
                    for (int pj = pi + 1; pj < sesionesDelDocente.Count; pj++)
                    {
                        var s1 = sesionesDelDocente[pi];
                        var s2 = sesionesDelDocente[pj];
                        int d1 = (int)Math.Ceiling(s1.DuracionHoras);
                        int d2 = (int)Math.Ceiling(s2.DuracionHoras);

                        // s1 termina antes de que empiece s2, O viceversa
                        var s1First = model.NewBoolVar($"docOrd_{s1.Id:N}_{s2.Id:N}");
                        model.Add(timeVars[s1.Id] + d1 <= timeVars[s2.Id]).OnlyEnforceIf(s1First);
                        model.Add(timeVars[s2.Id] + d2 <= timeVars[s1.Id]).OnlyEnforceIf(s1First.Not());
                    }
                }
            }

            // HC-I02: Disponibilidad del Docente — todos los bloques de la sesión deben estar disponibles
            foreach (var sesion in sesiones)
            {
                if (!docenteDict.TryGetValue(sesion.DocenteId, out var docente)) continue;

                var bloquesDocente = docente.BloquesDisponibles;
                if (bloquesDocente.Count == 0)
                {
                    _logger.LogWarning("HC-I02: Docente {D} sin bloques disponibles — no se aplica restricción de disponibilidad.", docente.NombreCompleto);
                    continue;
                }

                // Conjunto de índices disponibles para búsqueda rápida
                var disponiblesSet = bloquesDocente
                    .Where(b => bloqueIndex.ContainsKey(b.Id))
                    .Select(b => bloqueIndex[b.Id])
                    .ToHashSet();

                int d = (int)Math.Ceiling(sesion.DuracionHoras);

                // Bloque B es válido si todos los bloques [B, B+d) están disponibles Y en el mismo día
                var indicesPermitidos = disponiblesSet
                    .Where(startIdx =>
                    {
                        var startDia = bloques[startIdx].Dia;
                        return Enumerable.Range(0, d).All(k =>
                        {
                            int idx = startIdx + k;
                            return idx < bloques.Count
                                && disponiblesSet.Contains(idx)
                                && bloques[idx].Dia == startDia;
                        });
                    })
                    .Select(idx => (long)idx)
                    .OrderBy(x => x)
                    .ToArray();

                if (indicesPermitidos.Any())
                {
                    model.AddLinearExpressionInDomain(
                        timeVars[sesion.Id],
                        CpDomain.FromValues(indicesPermitidos));
                }
                else
                {
                    _logger.LogWarning("HC-I02: Docente {D} — ningún bloque disponible coincide con bloques canónicos del modelo.", docente.NombreCompleto);
                }
            }

            // HC-I03: Max Horas Semanales — pre-solve hard checks (session durations are fixed).
            foreach (var grupo in sesionesPorDocente)
            {
                if (!docenteDict.TryGetValue(grupo.Key, out var docente)) continue;

                var sesionesAsignadas  = grupo.Count();
                var bloquesDisponibles = docente.BloquesDisponibles.Count;

                if (bloquesDisponibles == 0)
                {
                    var msg = $"Docente {docente.NombreCompleto} no tiene disponibilidad definida — no puede recibir sesiones.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }

                if (sesionesAsignadas > bloquesDisponibles)
                {
                    var msg = $"Docente {docente.NombreCompleto} tiene {sesionesAsignadas} sesiones pero solo {bloquesDisponibles} bloques disponibles — imposible agendar sin solapamientos.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }

                var totalHoras = grupo.Sum(s => s.DuracionHoras);
                if (totalHoras > docente.MaximoHorasSemanales)
                {
                    var msg = $"Docente {docente.NombreCompleto} tiene {totalHoras}h asignadas, excede su máximo de {docente.MaximoHorasSemanales}h semanales.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, sesiones.AsReadOnly(), msg);
                }
            }

            // HC-S01: Conflicto de Espacio — mismo espacio no puede tener sesiones que se solapen en el tiempo
            if (spaceVars.Any())
            {
                for (int i = 0; i < sesiones.Count; i++)
                {
                    for (int j = i + 1; j < sesiones.Count; j++)
                    {
                        var s1 = sesiones[i];
                        var s2 = sesiones[j];

                        if (!spaceVars.ContainsKey(s1.Id) || !spaceVars.ContainsKey(s2.Id)) continue;

                        int d1 = (int)Math.Ceiling(s1.DuracionHoras);
                        int d2 = (int)Math.Ceiling(s2.DuracionHoras);

                        // sameSpace = (spaceVar[s1] == spaceVar[s2])
                        var sameSpace = model.NewBoolVar($"sameSpace_{i}_{j}");
                        model.Add(spaceVars[s1.Id] == spaceVars[s2.Id]).OnlyEnforceIf(sameSpace);
                        model.Add(spaceVars[s1.Id] != spaceVars[s2.Id]).OnlyEnforceIf(sameSpace.Not());

                        // Si mismo espacio: s1 termina antes de s2, O s2 termina antes de s1.
                        // OnlyEnforceIf no permite encadenado → auxiliares para (sameSpace ∧ orden).
                        var s1First = model.NewBoolVar($"spOrd_{i}_{j}");

                        // aux1 = sameSpace AND s1First
                        var aux1 = model.NewBoolVar($"aux1_{i}_{j}");
                        model.AddBoolAnd(new ILiteral[] { sameSpace, s1First }).OnlyEnforceIf(aux1);
                        model.AddBoolOr(new ILiteral[] { sameSpace.Not(), s1First.Not() }).OnlyEnforceIf(aux1.Not());
                        model.Add(timeVars[s1.Id] + d1 <= timeVars[s2.Id]).OnlyEnforceIf(aux1);

                        // aux2 = sameSpace AND NOT s1First
                        var aux2 = model.NewBoolVar($"aux2_{i}_{j}");
                        model.AddBoolAnd(new ILiteral[] { sameSpace, s1First.Not() }).OnlyEnforceIf(aux2);
                        model.AddBoolOr(new ILiteral[] { sameSpace.Not(), s1First }).OnlyEnforceIf(aux2.Not());
                        model.Add(timeVars[s2.Id] + d2 <= timeVars[s1.Id]).OnlyEnforceIf(aux2);
                    }
                }
            }

            // HC-S03: Tipo de Espacio — sesiones de laboratorio solo en espacios de tipo laboratorio
            if (spaceVars.Any() && espacios.Any())
            {
                foreach (var sesion in sesiones)
                {
                    if (sesion.EspacioId.HasValue && sesion.EspacioId != Guid.Empty)
                    {
                        var espacioOriginal = espacios.FirstOrDefault(e => e.Id == sesion.EspacioId);
                        if (espacioOriginal != null && espacioOriginal.Tipo == TipoEspacio.Laboratorio)
                        {
                            var labIndices = espacios
                                .Select((e, idx) => new { e, idx })
                                .Where(x => x.e.Tipo == TipoEspacio.Laboratorio)
                                .Select(x => (long)x.idx)
                                .ToArray();

                            if (labIndices.Any() && spaceVars.ContainsKey(sesion.Id))
                            {
                                model.AddLinearExpressionInDomain(
                                    spaceVars[sesion.Id],
                                    CpDomain.FromValues(labIndices));
                            }
                        }
                    }
                }
            }

            // --- RESOLVER ---
            try
            {
                System.IO.File.WriteAllText("cp_model_debug.txt", model.Model.ToString());
                _logger.LogInformation("Modelo CP-SAT exportado a cp_model_debug.txt para inspección de restricciones.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo exportar el modelo CP-SAT.");
            }

            var solver = new CpSolver();
            // log_search_progress:true activa la salida de OR-Tools a la consola para ver qué contradicciones encuentra
            solver.StringParameters = $"max_time_in_seconds:{TimeoutSegundos},log_search_progress:true";

            _logger.LogInformation("Resolviendo modelo CP-SAT (timeout: {T}s)...", TimeoutSegundos);
            var status = solver.Solve(model);

            _logger.LogInformation("CP-SAT terminó con status: {Status}", status);

            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                // Extraer solución y actualizar sesiones
                foreach (var sesion in sesiones)
                {
                    var bloqueIdx = (int)solver.Value(timeVars[sesion.Id]);
                    var bloqueAsignado = bloques[bloqueIdx];
                    sesion.AsignarBloqueTiempo(bloqueAsignado.Id);

                    if (spaceVars.ContainsKey(sesion.Id))
                    {
                        var espacioIdx = (int)solver.Value(spaceVars[sesion.Id]);
                        if (espacioIdx < espacios.Count)
                        {
                            var espacioAsignado = espacios[espacioIdx];
                            sesion.AsignarEspacio(espacioAsignado.Id);
                        }
                    }
                }

                _logger.LogInformation("Fase 2 completada exitosamente. Todas las sesiones tienen asignación factible.");
                return new ResultadoFactibilidad(true, sesiones.AsReadOnly(), "");
            }
            else
            {
                _logger.LogWarning("CP-SAT no encontró solución factible. Status: {Status}", status);
                return new ResultadoFactibilidad(false, sesiones.AsReadOnly(),
                    $"No se encontró solución factible. Status del solver: {status}. Revise las restricciones de docente, espacio y disponibilidad.");
            }
        }
    }
}
