using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Evalúa la función de aptitud (fitness) de un cromosoma basándose en las
    /// restricciones blandas priorizadas para el piloto.
    /// Menor fitness = mejor horario.
    /// </summary>
    public class EvaluadorFitness
    {
        private readonly List<Sesion> _sesiones;
        private readonly List<BloqueTiempo> _bloques;
        private readonly List<Docente> _docentes;

        private readonly int _pesoSC01;
        private readonly int _pesoSC06;
        private readonly int _pesoSC09;

        public EvaluadorFitness(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Docente> docentes,
            ConfiguracionOptimizacion? config = null)
        {
            _sesiones = sesiones;
            _bloques  = bloques;
            _docentes = docentes;
            _pesoSC01 = config?.PesoErgo     ?? 3;
            _pesoSC06 = config?.PesoTiempos  ?? 2;
            _pesoSC09 = config?.PesoAlmuerzo ?? 1;
        }

        public decimal Evaluar(CromosomaHorario cromosoma)
        {
            decimal fitness = 0;

            fitness += _pesoSC01 * EvaluarSC01_HorarioCompactoDocente(cromosoma);
            fitness += _pesoSC06 * EvaluarSC06_CargaDistribuida(cromosoma);
            fitness += _pesoSC09 * EvaluarSC09_EvitarMaxHorasDiarias(cromosoma);

            return fitness;
        }

        /// <summary>
        /// SC-01: Minimizar huecos inactivos entre sesiones del mismo docente en un día.
        /// Usa la duración real de cada sesión para calcular su hora de fin efectiva.
        /// </summary>
        private int EvaluarSC01_HorarioCompactoDocente(CromosomaHorario cromosoma)
        {
            int totalHuecos = 0;

            // Fix #7: pair each gene with its block AND its session duration
            var genesPorDocente = new Dictionary<Guid, List<(BloqueTiempo bloque, decimal duracion)>>();
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docenteId = _sesiones[i].DocenteId;
                var bloqueIdx = cromosoma.BloqueIndices[i];
                if (bloqueIdx < 0 || bloqueIdx >= _bloques.Count) continue;

                if (!genesPorDocente.ContainsKey(docenteId))
                    genesPorDocente[docenteId] = new List<(BloqueTiempo, decimal)>();
                genesPorDocente[docenteId].Add((_bloques[bloqueIdx], _sesiones[i].DuracionHoras));
            }

            foreach (var par in genesPorDocente)
            {
                var bloquesPorDia = par.Value.GroupBy(x => x.bloque.Dia);

                foreach (var dia in bloquesPorDia)
                {
                    var ordenados = dia.OrderBy(x => x.bloque.HoraInicio).ToList();
                    for (int i = 1; i < ordenados.Count; i++)
                    {
                        // Effective end of previous session = start + actual duration
                        var finAnterior = ordenados[i - 1].bloque.HoraInicio
                            .Add(TimeSpan.FromHours((double)ordenados[i - 1].duracion));
                        var gap = ordenados[i].bloque.HoraInicio.ToTimeSpan() - finAnterior.ToTimeSpan();
                        if (gap.TotalHours > 0)
                            totalHuecos += (int)Math.Ceiling(gap.TotalHours);
                    }
                }
            }

            return totalHuecos;
        }

        /// <summary>
        /// SC-06: Distribución uniforme de la carga del docente a lo largo de los días.
        /// Retorna la suma de desviaciones respecto a la media.
        /// </summary>
        private int EvaluarSC06_CargaDistribuida(CromosomaHorario cromosoma)
        {
            int penalizacion = 0;

            var horasPorDocentePorDia = new Dictionary<Guid, Dictionary<int, decimal>>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docenteId = _sesiones[i].DocenteId;
                var bloqueIdx = cromosoma.BloqueIndices[i];
                if (bloqueIdx < 0 || bloqueIdx >= _bloques.Count) continue;

                var dia = (int)_bloques[bloqueIdx].Dia;
                var duracion = _sesiones[i].DuracionHoras;

                if (!horasPorDocentePorDia.ContainsKey(docenteId))
                    horasPorDocentePorDia[docenteId] = new Dictionary<int, decimal>();

                if (!horasPorDocentePorDia[docenteId].ContainsKey(dia))
                    horasPorDocentePorDia[docenteId][dia] = 0;

                horasPorDocentePorDia[docenteId][dia] += duracion;
            }

            foreach (var docente in horasPorDocentePorDia)
            {
                var horas = docente.Value.Values.ToList();
                if (horas.Count <= 1) continue;

                var media = horas.Average(h => (double)h);
                var desviacion = horas.Sum(h => Math.Abs((double)h - media));
                penalizacion += (int)Math.Ceiling(desviacion);
            }

            return penalizacion;
        }

        /// <summary>
        /// SC-09: Evitar que un docente ocupe más del 80% de las horas disponibles en un día.
        /// Penaliza si las horas asignadas superan el umbral respecto al total de horas del día.
        /// </summary>
        private int EvaluarSC09_EvitarMaxHorasDiarias(CromosomaHorario cromosoma)
        {
            int penalizacion = 0;

            // Count of 1-hour slots per day == total available hours per day
            var horasDisponiblesPorDia = _bloques.GroupBy(b => b.Dia)
                .ToDictionary(g => (int)g.Key, g => g.Count());

            // Fix #6: accumulate occupied HOURS (session duration), not session count
            var horasPorDocenteDia = new Dictionary<Guid, Dictionary<int, decimal>>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docenteId = _sesiones[i].DocenteId;
                var bloqueIdx = cromosoma.BloqueIndices[i];
                if (bloqueIdx < 0 || bloqueIdx >= _bloques.Count) continue;

                var dia = (int)_bloques[bloqueIdx].Dia;

                if (!horasPorDocenteDia.ContainsKey(docenteId))
                    horasPorDocenteDia[docenteId] = new Dictionary<int, decimal>();

                if (!horasPorDocenteDia[docenteId].ContainsKey(dia))
                    horasPorDocenteDia[docenteId][dia] = 0;

                horasPorDocenteDia[docenteId][dia] += _sesiones[i].DuracionHoras;
            }

            foreach (var docente in horasPorDocenteDia)
            {
                foreach (var dia in docente.Value)
                {
                    if (horasDisponiblesPorDia.TryGetValue(dia.Key, out var totalHorasDia) && totalHorasDia > 0)
                    {
                        var ocupacion = (double)dia.Value / totalHorasDia;
                        if (ocupacion > 0.8)
                            penalizacion++;
                    }
                }
            }

            return penalizacion;
        }
    }
}
