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
        /// <param name="sesionesFijasIds">
        /// BASE1 (regla 8, CLAUDE.md): ids de sesiones del horario base. Sus bloques NUNCA se
        /// reasignan — llegan con <see cref="Sesion.BloqueTiempoId"/> ya fijado por el usuario y
        /// este motor solo las usa para reservar ese bloque frente a sus vecinas en el grafo de
        /// conflictos. Antes se coloreaban como cualquier otra sesión y el warm-start las movía de
        /// su bloque pedido; CP-SAT fijaba luego la igualdad al valor YA MOVIDO, no al original.
        /// </param>
        Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloquesDisponibles,
            IEnumerable<Grupo>? grupos = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            IReadOnlySet<Guid>? sesionesFijasIds = null,
            CancellationToken ct = default);
    }
}
