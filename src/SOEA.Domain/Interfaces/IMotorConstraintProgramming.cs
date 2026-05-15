using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Resultado de la Fase 2 (Constraint Programming).
    /// </summary>
    public record ResultadoFactibilidad(
        bool EsFactible,
        IReadOnlyList<Sesion> SesionesResueltas,
        string MensajeError);

    /// <summary>
    /// Motor de Constraint Programming (Fase 2).
    /// Toma la salida de la Fase 1 y usa CP-SAT para imponer todas las restricciones duras.
    /// </summary>
    public interface IMotorConstraintProgramming
    {
        Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes);
    }
}
