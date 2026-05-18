using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Motor de Algoritmo Genético (Fase 3).
    /// Optimiza restricciones blandas a partir de la solución factible de la Fase 2.
    /// </summary>
    public class MotorGenetico : IMotorGenetico
    {
        private readonly ILogger<MotorGenetico> _logger;

        // Hiperparámetros (configurables en el futuro)
        private const int TamañoPoblacion = 50;
        private const int MaxGeneraciones = 200;
        private const int TamañoTorneo = 5;
        private const double ProbabilidadCruce = 0.8;
        private const double ProbabilidadMutacion = 0.05;
        private const int UmbralConvergencia = 30;

        public MotorGenetico(ILogger<MotorGenetico> logger)
        {
            _logger = logger;
        }

        public Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion> sesionesFactibles,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes)
        {
            var resultado = OptimizarSincrono(
                sesionesFactibles.ToList(),
                bloques.ToList(),
                espacios.ToList(),
                docentes.ToList());
            return Task.FromResult(resultado);
        }

        private ResultadoOptimizacion OptimizarSincrono(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Espacio> espacios,
            List<Docente> docentes)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques para la Fase 3.");
                return new ResultadoOptimizacion(sesiones.AsReadOnly(), 0, 0);
            }

            _logger.LogInformation("Fase 3 (Genético): Iniciando con {S} sesiones, población={P}, maxGen={G}.",
                sesiones.Count, TamañoPoblacion, MaxGeneraciones);

            var rng = new Random(42);
            var bloqueIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;

            var espacioIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < espacios.Count; i++) espacioIndex[espacios[i].Id] = i;

            // Crear cromosoma semilla desde la solución factible de la Fase 2
            var sesionIds = sesiones.Select(s => s.Id).ToArray();
            var bloqueIndices = sesiones.Select(s =>
                s.BloqueTiempoId != Guid.Empty && bloqueIndex.ContainsKey(s.BloqueTiempoId)
                    ? bloqueIndex[s.BloqueTiempoId] : 0).ToArray();
            var espacioIndices = sesiones.Select(s =>
                s.EspacioId.HasValue && s.EspacioId != Guid.Empty && espacioIndex.ContainsKey(s.EspacioId.Value)
                    ? espacioIndex[s.EspacioId.Value] : 0).ToArray();

            var semilla = new CromosomaHorario(sesionIds, bloqueIndices, espacioIndices);

            var evaluador = new EvaluadorFitness(sesiones, bloques, docentes);
            var operadores = new OperadoresGeneticos(sesiones, bloques.Count, espacios.Count, rng);

            // Inicializar población
            var poblacion = new List<(CromosomaHorario cromosoma, decimal fitness)>();
            poblacion.Add((semilla, evaluador.Evaluar(semilla)));

            for (int i = 1; i < TamañoPoblacion; i++)
            {
                var perturbado = semilla.ClonarYPerturbar(rng, bloques.Count, espacios.Count, perturbaciones: 2 + rng.Next(3));
                operadores.Reparar(perturbado);
                poblacion.Add((perturbado, evaluador.Evaluar(perturbado)));
            }

            decimal mejorFitnessGlobal = poblacion.Min(p => p.fitness);
            int generacionesSinMejora = 0;
            int generacionFinal = 0;

            _logger.LogInformation("Fitness inicial: {F}", mejorFitnessGlobal);

            // Ciclo generacional
            for (int gen = 1; gen <= MaxGeneraciones; gen++)
            {
                var padre1 = operadores.SeleccionTorneo(poblacion, TamañoTorneo);
                var padre2 = operadores.SeleccionTorneo(poblacion, TamañoTorneo);

                var hijo = operadores.Cruce(padre1, padre2, ProbabilidadCruce);
                operadores.Mutar(hijo, ProbabilidadMutacion);
                operadores.Reparar(hijo);

                var fitnessHijo = evaluador.Evaluar(hijo);

                // Reemplazar el peor de la población si el hijo es mejor
                int peorIdx = 0;
                decimal peorFitness = poblacion[0].fitness;
                for (int i = 1; i < poblacion.Count; i++)
                {
                    if (poblacion[i].fitness > peorFitness)
                    {
                        peorFitness = poblacion[i].fitness;
                        peorIdx = i;
                    }
                }

                if (fitnessHijo < peorFitness)
                {
                    poblacion[peorIdx] = (hijo, fitnessHijo);
                }

                var mejorActual = poblacion.Min(p => p.fitness);
                if (mejorActual < mejorFitnessGlobal)
                {
                    mejorFitnessGlobal = mejorActual;
                    generacionesSinMejora = 0;
                }
                else
                {
                    generacionesSinMejora++;
                }

                generacionFinal = gen;

                if (generacionesSinMejora >= UmbralConvergencia)
                {
                    _logger.LogInformation("Convergencia alcanzada en generación {G}. Fitness: {F}", gen, mejorFitnessGlobal);
                    break;
                }

                if (gen % 50 == 0)
                {
                    _logger.LogDebug("Generación {G}: mejor fitness = {F}", gen, mejorFitnessGlobal);
                }
            }

            // Obtener el mejor cromosoma
            var mejorCromosoma = poblacion.OrderBy(p => p.fitness).First().cromosoma;

            // Aplicar las asignaciones del mejor cromosoma a las sesiones
            for (int i = 0; i < sesiones.Count; i++)
            {
                var bloqueIdx = mejorCromosoma.BloqueIndices[i];
                if (bloqueIdx >= 0 && bloqueIdx < bloques.Count)
                    sesiones[i].AsignarBloqueTiempo(bloques[bloqueIdx].Id);

                // Fix #3: also write back room assignments from the evolved chromosome
                if (sesiones[i].Modalidad != Modalidad.Virtual && espacios.Count > 0)
                {
                    var espacioIdx = mejorCromosoma.EspacioIndices[i];
                    if (espacioIdx >= 0 && espacioIdx < espacios.Count)
                        sesiones[i].AsignarEspacio(espacios[espacioIdx].Id);
                }
            }

            _logger.LogInformation("Fase 3 completada. Generaciones: {G}, Fitness final: {F}",
                generacionFinal, mejorFitnessGlobal);

            return new ResultadoOptimizacion(sesiones.AsReadOnly(), mejorFitnessGlobal, generacionFinal);
        }
    }
}
