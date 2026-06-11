using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Resultado de la Fase 2 (Constraint Programming).
    /// Cada sesión lógica factible produce dos <see cref="AsignacionSemanal"/> (Semana A y B).
    /// </summary>
    public record ResultadoFactibilidad(
        bool EsFactible,
        IReadOnlyList<AsignacionSemanal> Asignaciones,
        string MensajeError);

    /// <summary>
    /// Motor de Constraint Programming (Fase 2).
    /// Toma la salida de la Fase 1 y usa CP-SAT para imponer todas las restricciones duras.
    /// </summary>
    public interface IMotorConstraintProgramming
    {
        /// <param name="sesionesFijasIds">
        /// IDs de sesiones cuya franja ya está decidida (horario base).
        /// CP-SAT les añade una restricción de igualdad en vez de un hint — no se mueven.
        /// </param>
        Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes,
            IEnumerable<Guid>? sesionesFijasIds = null);
    }
}
