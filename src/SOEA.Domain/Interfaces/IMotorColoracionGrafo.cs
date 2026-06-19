using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IMotorColoracionGrafo
    {
        /// <summary>
        /// Asigna bloques de tiempo iniciales a un conjunto de sesiones
        /// utilizando un algoritmo de coloración de grafos (Welsh-Powell).
        /// Garantiza no asignar el mismo bloque de tiempo a sesiones que entran en conflicto.
        /// </summary>
        Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloquesDisponibles, CancellationToken ct = default);
    }
}
