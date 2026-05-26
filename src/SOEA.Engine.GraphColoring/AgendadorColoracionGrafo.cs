using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Engine.GraphColoring
{
    /// <summary>
    /// Fase 1 — Welsh-Powell duration-aware.
    /// Asigna a cada sesión un span [startIdx, startIdx+DuracionHoras) que no
    /// cruza día y no colisiona con los spans de vecinos del grafo de conflictos.
    /// La salida sirve como warm-start para CP-SAT (no es vinculante).
    /// </summary>
    public class AgendadorColoracionGrafo : IMotorColoracionGrafo
    {
        private readonly ConstructorGrafoConflictos _constructorGrafo;
        private readonly ILogger<AgendadorColoracionGrafo> _logger;

        public AgendadorColoracionGrafo(ConstructorGrafoConflictos constructorGrafo, ILogger<AgendadorColoracionGrafo> logger)
        {
            _constructorGrafo = constructorGrafo;
            _logger = logger;
        }

        public Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloquesDisponibles)
        {
            var resultado = AsignarBloquesSincrono(sesiones.ToList(), bloquesDisponibles.ToList());
            return Task.FromResult<IEnumerable<Sesion>>(resultado);
        }

        private List<Sesion> AsignarBloquesSincrono(List<Sesion> sesiones, List<BloqueTiempo> bloques)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques disponibles para procesar.");
                return sesiones;
            }

            _logger.LogInformation(
                "Welsh-Powell duration-aware: {S} sesiones, {B} bloques.",
                sesiones.Count, bloques.Count);

            // 1. Grafo de conflictos (estructural: comparten docente o espacio)
            var grafo = _constructorGrafo.Construir(sesiones);

            // 2. Helpers de planeación temporal
            var rangosPorDia = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx    = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // 3. Pre-calcular duración por sesión y starts candidatos (que caben en algún día)
            var duraciones = new Dictionary<Guid, int>();
            var startsValidosPorSesion = new Dictionary<Guid, int[]>();
            foreach (var s in sesiones)
            {
                int dur = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                duraciones[s.Id] = dur;
                startsValidosPorSesion[s.Id] = BloquesPlanner
                    .StartsValidos(dur, bloques.Count, rangosPorDia, diaPorIdx, bloquesDisponibles: null)
                    .ToArray();
            }

            // 4. Orden Welsh-Powell: grado DESC, romper empates por duración DESC.
            var sesionesOrdenadas = sesiones
                .OrderByDescending(s => grafo[s.Id].Count)
                .ThenByDescending(s => s.DuracionHoras)
                .ToList();
            // 5. Coloreado: para cada sesión, buscar el primer start cuyo span
            //    no intersecte el span de ningún vecino ya colocado.
            var bloquesOcupadosPorSesion = new Dictionary<Guid, HashSet<int>>();
            int asignadas = 0, enConflicto = 0;
            foreach (var sesion in sesionesOrdenadas)
            {
                int dur = duraciones[sesion.Id];
                var vecinos = grafo[sesion.Id];

                // Unir todos los índices ocupados por los vecinos ya coloreados.
                var bloqueado = new HashSet<int>();
                foreach (var vId in vecinos)
                {
                    if (bloquesOcupadosPorSesion.TryGetValue(vId, out var ocupados))
                        bloqueado.UnionWith(ocupados);
                }

                int? startAsignado = null;
                foreach (var start in startsValidosPorSesion[sesion.Id])
                {
                    bool libre = true;
                    for (int k = 0; k < dur; k++)
                    {
                        if (bloqueado.Contains(start + k)) { libre = false; break; }
                    }
                    if (libre) { startAsignado = start; break; }
                }

                if (startAsignado.HasValue)
                {
                    var ocupa = new HashSet<int>();
                    for (int k = 0; k < dur; k++) ocupa.Add(startAsignado.Value + k);
                    bloquesOcupadosPorSesion[sesion.Id] = ocupa;
                    sesion.AsignarBloqueTiempo(bloques[startAsignado.Value].Id);
                    asignadas++;
                }
                else
                {
                    sesion.MarcarConConflicto("No se encontró un span de bloques sin conflictos para la duración requerida.");
                    enConflicto++;
                }
            }

            _logger.LogInformation(
                "Coloreado completado: {A} asignadas, {C} en conflicto (CP-SAT las re-evaluará).",
                asignadas, enConflicto);

            return sesiones;
        }
    }
}
