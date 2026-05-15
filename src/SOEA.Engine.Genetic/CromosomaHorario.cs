using System;
using System.Collections.Generic;
using System.Linq;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Cromosoma que codifica una asignación completa del horario.
    /// Cada gen es una terna (SesionId, BloqueIndex, EspacioIndex).
    /// </summary>
    public class CromosomaHorario
    {
        public int[] BloqueIndices { get; }
        public int[] EspacioIndices { get; }
        public Guid[] SesionIds { get; }
        public int CantidadGenes => SesionIds.Length;

        public CromosomaHorario(Guid[] sesionIds, int[] bloqueIndices, int[] espacioIndices)
        {
            SesionIds = sesionIds;
            BloqueIndices = bloqueIndices;
            EspacioIndices = espacioIndices;
        }

        /// <summary>
        /// Crea una copia profunda del cromosoma.
        /// </summary>
        public CromosomaHorario Clonar()
        {
            return new CromosomaHorario(
                (Guid[])SesionIds.Clone(),
                (int[])BloqueIndices.Clone(),
                (int[])EspacioIndices.Clone());
        }

        /// <summary>
        /// Crea una copia perturbada: intercambia aleatoriamente N genes de bloque.
        /// </summary>
        public CromosomaHorario ClonarYPerturbar(Random rng, int maxBloques, int perturbaciones = 3)
        {
            var clon = Clonar();
            for (int i = 0; i < perturbaciones && clon.CantidadGenes > 0; i++)
            {
                var idx = rng.Next(clon.CantidadGenes);
                clon.BloqueIndices[idx] = rng.Next(maxBloques);
            }
            return clon;
        }
    }
}
