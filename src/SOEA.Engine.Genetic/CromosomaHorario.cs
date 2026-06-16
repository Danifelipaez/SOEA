using System;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Cromosoma del modelo bi-semanal (Incremento 2). Dos genes de inicio por sesión:
    /// <see cref="Start"/> (Semana A) y <see cref="StartB"/> (Semana B). Invariante mantenido por
    /// los operadores y la reparación: para TipoA/TipoB, <c>StartB[i] == Start[i]</c> siempre
    /// (regla 9 / ALT-05 — misma franja en ambas semanas). Para SinAlternancia, StartB[i] puede
    /// diferir de Start[i] (ALT-06).
    ///
    /// Los espacios NO van en el cromosoma: ninguno de los objetivos blandos (huecos, horas
    /// seguidas, balance entre días/semanas) depende del aula. La asignación de aulas es un pase
    /// determinista posterior (<see cref="AsignadorEspacios"/>) que solo garantiza HC-S01/S03.
    ///
    /// <see cref="Start"/>[i] / <see cref="StartB"/>[i] son índices en la lista canónica de
    /// bloques (no un Id), paralelos a <see cref="SesionIds"/>.
    /// </summary>
    public class CromosomaHorario
    {
        public Guid[] SesionIds { get; }
        public int[]  Start     { get; }
        public int[]  StartB    { get; }
        public int CantidadGenes => SesionIds.Length;

        public CromosomaHorario(Guid[] sesionIds, int[] start, int[]? startB = null)
        {
            SesionIds = sesionIds;
            Start     = start;
            StartB    = startB ?? (int[])start.Clone();
        }

        public CromosomaHorario Clonar() =>
            new((Guid[])SesionIds.Clone(), (int[])Start.Clone(), (int[])StartB.Clone());
    }
}
