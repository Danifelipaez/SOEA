using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;

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

        // Pesos de restricciones blandas (configurables)
        private const int PesoSC01 = 3; // Horario compacto docente
        private const int PesoSC06 = 2; // Carga distribuida
        private const int PesoSC09 = 1; // Evitar max horas diarias

        public EvaluadorFitness(List<Sesion> sesiones, List<BloqueTiempo> bloques, List<Docente> docentes)
        {
            _sesiones = sesiones;
            _bloques = bloques;
            _docentes = docentes;
        }

        public decimal Evaluar(CromosomaHorario cromosoma)
        {
            decimal fitness = 0;

            fitness += PesoSC01 * EvaluarSC01_HorarioCompactoDocente(cromosoma);
            fitness += PesoSC06 * EvaluarSC06_CargaDistribuida(cromosoma);
            fitness += PesoSC09 * EvaluarSC09_EvitarMaxHorasDiarias(cromosoma);

            return fitness;
        }

        /// <summary>
        /// SC-01: Minimizar huecos inactivos entre sesiones del mismo docente en un día.
        /// Retorna el total de huecos detectados.
        /// </summary>
        private int EvaluarSC01_HorarioCompactoDocente(CromosomaHorario cromosoma)
        {
            int totalHuecos = 0;

            // Agrupar genes por docente
            var genesPorDocente = new Dictionary<Guid, List<int>>();
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docenteId = _sesiones[i].DocenteId;
                if (!genesPorDocente.ContainsKey(docenteId))
                    genesPorDocente[docenteId] = new List<int>();
                genesPorDocente[docenteId].Add(cromosoma.BloqueIndices[i]);
            }

            foreach (var par in genesPorDocente)
            {
                // Agrupar bloques asignados por día
                var bloquesPorDia = par.Value
                    .Where(bi => bi >= 0 && bi < _bloques.Count)
                    .Select(bi => _bloques[bi])
                    .GroupBy(b => b.Dia);

                foreach (var dia in bloquesPorDia)
                {
                    var horas = dia.OrderBy(b => b.HoraInicio).ToList();
                    for (int i = 1; i < horas.Count; i++)
                    {
                        var gap = horas[i].HoraInicio - horas[i - 1].HoraFin;
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
        /// SC-09: Evitar que un docente tenga asignadas todas las horas posibles en un día.
        /// Penaliza si un docente ocupa más del 80% de los bloques de un día.
        /// </summary>
        private int EvaluarSC09_EvitarMaxHorasDiarias(CromosomaHorario cromosoma)
        {
            int penalizacion = 0;

            var bloquesPorDia = _bloques.GroupBy(b => b.Dia).ToDictionary(g => (int)g.Key, g => g.Count());

            var asignacionesPorDocenteDia = new Dictionary<Guid, Dictionary<int, int>>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docenteId = _sesiones[i].DocenteId;
                var bloqueIdx = cromosoma.BloqueIndices[i];
                if (bloqueIdx < 0 || bloqueIdx >= _bloques.Count) continue;

                var dia = (int)_bloques[bloqueIdx].Dia;

                if (!asignacionesPorDocenteDia.ContainsKey(docenteId))
                    asignacionesPorDocenteDia[docenteId] = new Dictionary<int, int>();

                if (!asignacionesPorDocenteDia[docenteId].ContainsKey(dia))
                    asignacionesPorDocenteDia[docenteId][dia] = 0;

                asignacionesPorDocenteDia[docenteId][dia]++;
            }

            foreach (var docente in asignacionesPorDocenteDia)
            {
                foreach (var dia in docente.Value)
                {
                    if (bloquesPorDia.TryGetValue(dia.Key, out var totalBloquesDia) && totalBloquesDia > 0)
                    {
                        var ocupacion = (double)dia.Value / totalBloquesDia;
                        if (ocupacion > 0.8)
                            penalizacion++;
                    }
                }
            }

            return penalizacion;
        }
    }
}
