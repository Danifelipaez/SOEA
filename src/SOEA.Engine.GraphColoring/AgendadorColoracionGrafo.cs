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

            // Lookups adicionales para asignación consciente de duración
            var bloqueByDiaHora = bloquesDisponibles.ToDictionary(b => (b.Dia, b.HoraInicio));
            var sesionById = sesiones.ToDictionary(s => s.Id);

            // Estructura para mapeo rápido en memoria O(1)
            var asignaciones = new Dictionary<Guid, Guid>();

            _logger.LogInformation("Iniciando coloreado de nodos (sesiones)...");
            int asignadas = 0;
            int enConflicto = 0;

            // 3. Coloreado
            foreach (var sesion in sesionesOrdenadas)
            {
                var conflictos = grafo[sesion.Id];
                int dActual = (int)Math.Ceiling(sesion.DuracionHoras);

                // Calcular bloques prohibidos considerando duración de cada vecino y de la sesión actual.
                // Un bloque B está prohibido si el rango [B, B+dActual) se solapa con el rango del vecino.
                var bloquesProhibidos = new HashSet<Guid>();
                foreach (var vecinoId in conflictos)
                {
                    if (!asignaciones.TryGetValue(vecinoId, out var bloqueVecinoId)) continue;
                    if (!diccionarioBloques.TryGetValue(bloqueVecinoId, out var bloqueVecino)) continue;

                    int dVecino = sesionById.TryGetValue(vecinoId, out var sesVecino)
                        ? (int)Math.Ceiling(sesVecino.DuracionHoras)
                        : 1;

                    // Solapamiento: B < vecinoStart+dVecino  Y  vecinoStart < B+dActual
                    // → offset ∈ [-(dActual-1), dVecino-1] da los bloques B = vecinoStart+offset prohibidos
                    for (int offset = -(dActual - 1); offset < dVecino; offset++)
                    {
                        var horaProhibida = bloqueVecino.HoraInicio.AddHours(offset);
                        if (bloqueByDiaHora.TryGetValue((bloqueVecino.Dia, horaProhibida), out var bloqueProhibido))
                            bloquesProhibidos.Add(bloqueProhibido.Id);
                    }
                }

                // Asignar el primer bloque disponible donde la sesión cabe completamente en el mismo día
                Guid? bloqueAsignado = null;
                foreach (var bloqueId in bloquesIds)
                {
                    if (bloquesProhibidos.Contains(bloqueId)) continue;

                    var bloque = diccionarioBloques[bloqueId];
                    bool cabe = true;
                    for (int k = 1; k < dActual; k++)
                    {
                        if (!bloqueByDiaHora.ContainsKey((bloque.Dia, bloque.HoraInicio.AddHours(k))))
                        {
                            cabe = false;
                            break;
                        }
                    }
                    if (!cabe) continue;

                    bloqueAsignado = bloqueId;
                    break;
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
                    sesion.MarcarConConflicto("No se encontró un bloque de tiempo sin conflictos de Docente, Espacio o Asignatura.");
                    enConflicto++;
                }
            }

            _logger.LogInformation("Asignación de bloques de tiempo (Coloración) completada.");
            _logger.LogInformation("Resultados: {Asignadas} sesiones asignadas exitosamente, {EnConflicto} sesiones sin bloque asignado (marcado en Conflicto).", asignadas, enConflicto);

            return sesiones;
        }
    }
}
