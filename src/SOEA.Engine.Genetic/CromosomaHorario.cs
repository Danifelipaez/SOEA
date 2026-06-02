using System;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Cromosoma del modelo bi-semanal. La ÚNICA variable de decisión del GA es el bloque de
    /// inicio de cada sesión, COMPARTIDO por las semanas A y B (regla 9 satisfecha por
    /// construcción: no hay genes A/B separados que puedan desincronizarse).
    ///
    /// Los espacios NO van en el cromosoma: ninguno de los objetivos blandos (huecos, horas
    /// seguidas, balance entre días) depende del aula. La asignación de aulas es un pase
    /// determinista posterior (<see cref="AsignadorEspacios"/>) que solo garantiza HC-S01/S03.
    ///
    /// <see cref="Start"/>[i] es un índice en la lista canónica de bloques (no un Id), paralelo
    /// a <see cref="SesionIds"/>.
    /// </summary>
    public class CromosomaHorario
    {
        public Guid[] SesionIds { get; }
        public int[]  Start     { get; }
        public int CantidadGenes => SesionIds.Length;

        public CromosomaHorario(Guid[] sesionIds, int[] start)
        {
            SesionIds = sesionIds;
            Start     = start;
        }

        public CromosomaHorario Clonar() =>
            new((Guid[])SesionIds.Clone(), (int[])Start.Clone());
    }
}
