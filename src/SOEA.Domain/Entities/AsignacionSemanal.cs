using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Materialización de una <see cref="Sesion"/> en una semana concreta del ciclo de alternancia.
    /// Cada sesión lógica produce dos asignaciones (una por <see cref="SemanaAcademica"/>), que pueden
    /// diferir en franja, espacio y modalidad. La <see cref="Sesion"/> permanece como unidad lógica intacta.
    /// </summary>
    public class AsignacionSemanal : EntidadBase
    {
        public Guid SesionId { get; private set; }
        public SemanaAcademica Semana { get; private set; }
        public Guid BloqueTiempoId { get; private set; }
        public Guid? EspacioId { get; private set; }
        public Modalidad Modalidad { get; private set; }

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
