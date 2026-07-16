using System;
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
        /// <param name="grupos">
        /// HC-G01 (opcional): si se pasan, el dominio de inicios se confina a la franja declarada
        /// del grupo — misma fuente (<c>CalculadorDominioSesion</c>) que CP-SAT y el GA, para que
        /// el warm-start nunca caiga fuera del dominio que Fase 2 va a exigir.
        /// </param>
        /// <param name="ventanaPorAsignatura">HC-VH (opcional): ventana horaria por asignatura, misma fuente que Fase 2/3.</param>
        Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloquesDisponibles,
            IEnumerable<Grupo>? grupos = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            CancellationToken ct = default);
    }
}
