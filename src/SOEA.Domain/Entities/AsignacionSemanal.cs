using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Materialización de una <see cref="Sesion"/>: su franja, su aula y su modalidad.
    ///
    /// Cada sesión lógica produce UNA asignación, en su semana canónica — la semana en la que es
    /// presencial (ver <see cref="Services.ModalidadSemanal.SemanaCanonica"/>). Para una sesión que
    /// no alterna, <see cref="SemanaAcademica.A"/> significa "todas las semanas", no "solo la impar":
    /// la franja y el aula son un dato único del semestre (regla 9 / ALT-05).
    ///
    /// Antes se producían DOS asignaciones por sesión, una por semana, y nada obligaba a que
    /// coincidieran salvo para TipoA/TipoB: el resultado eran dos horarios incompatibles
    /// presentados como uno. La contraparte virtual de una sesión que alterna ya no se persiste
    /// (no reserva aula, así que en BD era ruido); se deriva al construir el DTO.
    /// </summary>
    public class AsignacionSemanal : EntidadBase
    {
        public Guid SesionId { get; private set; }
        public SemanaAcademica Semana { get; private set; }
        public Guid BloqueTiempoId { get; private set; }
        public Guid? EspacioId { get; private set; }
        public Modalidad Modalidad { get; private set; }

        /// <summary>
        /// Horario (corrida de generación) al que pertenece la asignación. Acota el índice único de
        /// aula/bloque/semana: sin él, generar el horario de otro semestre chocaba contra las
        /// asignaciones vigentes del primero (NEW-3, auditoría 2026-09-28). Lo fija quien persiste
        /// (<see cref="AsignarHorario"/>); nulo solo en filas anteriores a esa migración o aún sin persistir.
        /// </summary>
        public Guid? HorarioId { get; private set; }

        // Constructor privado para EF Core
        private AsignacionSemanal() : base() { }

        public AsignacionSemanal(
            Guid id,
            Guid sesionId,
            SemanaAcademica semana,
            Guid bloqueTiempoId,
            Guid? espacioId,
            Modalidad modalidad) : base(id)
        {
            Validar(sesionId, bloqueTiempoId, espacioId, modalidad);

            SesionId = sesionId;
            Semana = semana;
            BloqueTiempoId = bloqueTiempoId;
            EspacioId = espacioId;
            Modalidad = modalidad;
        }

        public void AsignarHorario(Guid horarioId)
        {
            if (horarioId == Guid.Empty)
                throw new ArgumentException("El ID del horario no puede ser vacío.", nameof(horarioId));
            HorarioId = horarioId;
        }

        private static void Validar(Guid sesionId, Guid bloqueTiempoId, Guid? espacioId, Modalidad modalidad)
        {
            if (sesionId == Guid.Empty)
                throw new ArgumentException("El ID de la sesión no puede ser vacío.", nameof(sesionId));
            if (bloqueTiempoId == Guid.Empty)
                throw new ArgumentException("El bloque de tiempo asignado no puede ser vacío.", nameof(bloqueTiempoId));

            // Invariante regla 9: una asignación virtual nunca ocupa espacio físico.
            if (modalidad == Modalidad.Virtual && espacioId != null)
                throw new ArgumentException("Una asignación virtual no puede tener espacio asignado (EspacioId debe ser null).", nameof(espacioId));
        }
    }
}
