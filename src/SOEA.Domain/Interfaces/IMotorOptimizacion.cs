using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IMotorOptimizacion
    {
        /// <summary>
        /// Ejecuta el proceso de optimización para generar un horario.
        /// </summary>
        /// <param name="semestre">El semestre para el cual generar el horario.</param>
        /// <returns>El horario generado con el puntaje de fitness y las violaciones calculadas.</returns>
        Task<Horario> GenerateScheduleAsync(string semestre);
    }
}
