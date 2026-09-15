using System;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Cromosoma del horario: UN gen de inicio por sesión. La franja de una sesión es un dato
    /// único que aplica a todas las semanas del semestre (regla 9 / ALT-05); solo la modalidad
    /// alterna, y eso lo deriva <see cref="Domain.Services.ModalidadSemanal"/> sin intervención
    /// del GA. Antes existía un segundo gen <c>StartB</c> que para SinAlternancia podía divergir
    /// de <c>Start</c>: producía una semana par con otro horario, no un horario con alternancia.
    ///
    /// Los espacios NO van en el cromosoma: ninguno de los objetivos blandos (huecos, horas
    /// seguidas, balance entre días) depende del aula. La asignación de aulas es un pase
    /// determinista posterior (<see cref="Domain.Interfaces.IAsignadorEspaciosExacto"/>) que
    /// garantiza HC-S01/S03/S05.
    ///
    /// <see cref="Start"/>[i] son índices en la lista canónica de bloques (no un Id), paralelos a
    /// <see cref="SesionIds"/>.
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
