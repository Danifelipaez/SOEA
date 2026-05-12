using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.GraphColoring
{
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
            // Ejecutar el algoritmo computacionalmente intensivo de forma asíncrona si la cantidad es alta, o retornar con Task.FromResult
            var resultado = AsignarBloquesSincrono(sesiones.ToList(), bloquesDisponibles.ToList());
            return Task.FromResult<IEnumerable<Sesion>>(resultado);
        }

        private List<Sesion> AsignarBloquesSincrono(List<Sesion> sesiones, List<BloqueTiempo> bloquesDisponibles)
        {
            if (!sesiones.Any() || !bloquesDisponibles.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques de tiempo disponibles para procesar.");
                return sesiones;
            }

            _logger.LogInformation("Iniciando algoritmo Welsh-Powell para agendar {CantidadSesiones} sesiones con {CantidadBloques} bloques disponibles.", sesiones.Count, bloquesDisponibles.Count);

            // 1. Construir grafo de conflictos
            _logger.LogInformation("Construyendo grafo de conflictos...");
            var grafo = _constructorGrafo.Construir(sesiones);

            // 2. Ordenar nodos (sesiones) por grado (cantidad de conflictos) en orden descendente.
            // Esto cumple con la heurística de Welsh-Powell para priorizar lo más restrictivo.
            var sesionesOrdenadas = sesiones
                .OrderByDescending(s => grafo[s.Id].Count)
                .ToList();

            var diccionarioBloques = bloquesDisponibles.ToDictionary(b => b.Id);
            var bloquesIds = bloquesDisponibles.Select(b => b.Id).ToList();
            
            // Estructura para mapeo rápido en memoria O(1)
            var asignaciones = new Dictionary<Guid, Guid>();

            _logger.LogInformation("Iniciando coloreado de nodos (sesiones)...");
            int asignadas = 0;
            int enConflicto = 0;

            // 3. Coloreado
            foreach (var sesion in sesionesOrdenadas)
            {
                var conflictos = grafo[sesion.Id];

                // Obtener colores (Bloques) ya usados por los vecinos de la sesión actual
                var bloquesUsadosPorVecinos = new HashSet<Guid>();
                foreach (var vecinoId in conflictos)
                {
                    if (asignaciones.TryGetValue(vecinoId, out var bloqueUsado))
                    {
                        bloquesUsadosPorVecinos.Add(bloqueUsado);
                    }
                }

                // Asignar el primer bloque de tiempo disponible (First Available TimeSlot)
                Guid? bloqueAsignado = null;
                foreach (var bloqueId in bloquesIds)
                {
                    if (!bloquesUsadosPorVecinos.Contains(bloqueId))
                    {
                        bloqueAsignado = bloqueId;
                        break;
                    }
                }

                if (bloqueAsignado.HasValue)
                {
                    asignaciones[sesion.Id] = bloqueAsignado.Value;
                    sesion.AsignarBloqueTiempo(bloqueAsignado.Value);
                    asignadas++;
                }
                else
                {
                    // Si no hay bloques disponibles para esta sesión, la marcamos como Conflicto.
                    // Será tratada posteriormente por la Fase 2 (CP-SAT)
                    sesion.MarcarConConflicto();
                    enConflicto++;
                }
            }

            _logger.LogInformation("Asignación de bloques de tiempo (Coloración) completada.");
            _logger.LogInformation("Resultados: {Asignadas} sesiones asignadas exitosamente, {EnConflicto} sesiones sin bloque asignado (marcado en Conflicto).", asignadas, enConflicto);

            return sesiones;
        }
    }
}
