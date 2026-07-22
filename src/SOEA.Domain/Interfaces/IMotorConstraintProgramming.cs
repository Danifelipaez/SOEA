using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Causa de infactibilidad reportada por la Fase 2, usada por el loop reactivo de cesión a
    /// alternancia (Etapa 2) para saber si vale la pena ceder una sesión más: solo reintenta cuando
    /// la causa es <see cref="Espacio"/> — ceder no ayuda contra ventana horaria o franja de grupo.
    /// </summary>
    public enum MotivoInfactibilidad
    {
        Ninguno,
        Espacio,
        VentanaHoraria,
        FranjaGrupo,
        Datos,
        Otro
    }

    /// <summary>
    /// Resultado de la Fase 2 (Constraint Programming).
    /// Cada sesión lógica factible produce dos <see cref="AsignacionSemanal"/> (Semana A y B).
    /// </summary>
    public record ResultadoFactibilidad(
        bool EsFactible,
        IReadOnlyList<AsignacionSemanal> Asignaciones,
        string MensajeError,
        MotivoInfactibilidad Motivo = MotivoInfactibilidad.Ninguno);

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
        /// <param name="grupos">
        /// Grupos de estudiantes con su disponibilidad horaria.
        /// HC-G01 (hard): si un grupo declara Disponibilidad, sus sesiones solo se pueden
        /// asignar en bloques que caigan dentro de esa franja (Matutino/Vespertino).
        /// </param>
        /// <param name="ventanaPorAsignatura">
        /// Ventana horaria por asignatura (HC-VH, hard): si una asignatura declara [Min, Max],
        /// sus sesiones solo se asignan a bloques contenidos en ese rango. Null = sin restricción.
        /// </param>
        Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes,
            IEnumerable<Grupo>? grupos = null,
            IEnumerable<Guid>? sesionesFijasIds = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            CancellationToken ct = default);
    }
}
