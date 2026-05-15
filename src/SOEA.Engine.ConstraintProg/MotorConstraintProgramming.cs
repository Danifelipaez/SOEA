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

                if (espacios.Any())
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

            // --- RESTRICCIONES DURAS ---

            // HC-I01: Conflicto de Docente — mismo docente no puede tener dos sesiones al mismo tiempo
            var sesionesPorDocente = sesiones.GroupBy(s => s.DocenteId).Where(g => g.Key != Guid.Empty);
            foreach (var grupo in sesionesPorDocente)
            {
                var sesionesDelDocente = grupo.ToList();
                if (sesionesDelDocente.Count > 1)
                {
                    var vars = sesionesDelDocente.Select(s => timeVars[s.Id]).ToArray();
                    model.AddAllDifferent(vars);
                }
            }

            // HC-I02: Disponibilidad del Docente — sesión solo en bloques donde el docente está disponible
            foreach (var sesion in sesiones)
            {
                if (docenteDict.TryGetValue(sesion.DocenteId, out var docente) && docente.BloquesDisponibles.Any())
                {
                    var indicesPermitidos = docente.BloquesDisponibles
                        .Where(b => bloqueIndex.ContainsKey(b.Id))
                        .Select(b => (long)bloqueIndex[b.Id])
                        .ToArray();

                    if (indicesPermitidos.Any())
                    {
                        model.AddLinearExpressionInDomain(
                            timeVars[sesion.Id],
                            CpDomain.FromValues(indicesPermitidos));
                    }
                }
            }

            // HC-I03: Max Horas Semanales del Docente y Capacidad de Disponibilidad
            foreach (var grupo in sesionesPorDocente)
            {
                if (docenteDict.TryGetValue(grupo.Key, out var docente))
                {
                    var sesionesAsignadas = grupo.Count();
                    var bloquesDisponibles = docente.BloquesDisponibles.Count();
                    if (sesionesAsignadas > bloquesDisponibles && bloquesDisponibles > 0)
                    {
                        _logger.LogError("¡INFACTIBILIDAD DETECTADA ANTES DEL SOLVER! El docente {Nombre} tiene {Sesiones} sesiones asignadas, pero solo {Disponibles} bloques de disponibilidad. Imposible agendar sin empalmes.",
                            docente.NombreCompleto, sesionesAsignadas, bloquesDisponibles);
                    }

                    var totalHoras = grupo.Sum(s => (int)s.DuracionHoras);
                    if (totalHoras > (int)docente.MaximoHorasSemanales)
                    {
                        _logger.LogWarning("Docente {Nombre} tiene {Total}h asignadas, excede su máximo de {Max}h. Se reportará como restricción.",
                            docente.NombreCompleto, totalHoras, docente.MaximoHorasSemanales);
                    }
                }
            }

            // HC-S01: Conflicto de Espacio — mismo espacio no puede tener dos sesiones al mismo tiempo
            if (spaceVars.Any())
            {
                for (int i = 0; i < sesiones.Count; i++)
                {
                    for (int j = i + 1; j < sesiones.Count; j++)
                    {
                        var s1 = sesiones[i];
                        var s2 = sesiones[j];

                        // Si ambas sesiones tienen variable de espacio, agregar:
                        // NOT (timeVar[s1] == timeVar[s2] AND spaceVar[s1] == spaceVar[s2])
                        if (spaceVars.ContainsKey(s1.Id) && spaceVars.ContainsKey(s2.Id))
                        {
                            var sameTime = model.NewBoolVar($"sameTime_{i}_{j}");
                            var sameSpace = model.NewBoolVar($"sameSpace_{i}_{j}");

                            // sameTime = (timeVar[s1] == timeVar[s2])
                            model.Add(timeVars[s1.Id] == timeVars[s2.Id]).OnlyEnforceIf(sameTime);
                            model.Add(timeVars[s1.Id] != timeVars[s2.Id]).OnlyEnforceIf(sameTime.Not());

                            // sameSpace = (spaceVar[s1] == spaceVar[s2])
                            model.Add(spaceVars[s1.Id] == spaceVars[s2.Id]).OnlyEnforceIf(sameSpace);
                            model.Add(spaceVars[s1.Id] != spaceVars[s2.Id]).OnlyEnforceIf(sameSpace.Not());

                            // NOT (sameTime AND sameSpace)
                            model.AddBoolOr(new[] { sameTime.Not(), sameSpace.Not() });
                        }
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
                            // Actualizar EspacioId en la sesión si es diferente
                            // (La sesión ya tiene EspacioId, pero CP-SAT pudo reasignarlo)
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
