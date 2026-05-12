using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Sesión académica: ocurrencia de una asignatura para un grupo/cohorte en un bloque de tiempo.
    /// Persistida como relación entre Asignatura, Docente, BloqueTiempo, Espacio (nullable), Grupo (nullable).
    /// </summary>
    public class Sesion : EntidadBase
    {
        public Guid AsignaturaId { get; private set; }
        public Guid DocenteId { get; private set; }
        public Guid BloqueTiempoId { get; private set; }
        public Guid? EspacioId { get; private set; }
        public Guid? GrupoId { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public Modalidad Modalidad { get; private set; }
        public EstadoSesion Estado { get; private set; }
        public decimal DuracionHoras { get; private set; }
        public bool EsBloque { get; private set; }
        public bool EstaDividida { get; private set; }

        // Constructor privado para EF Core
        private Sesion() : base() { }

        public Sesion(
            Guid id,
            Guid asignaturaId,
            Guid docenteId,
            Guid bloqueId,
            Guid? espacioId,
            Guid? grupoId,
            TipoAlternancia alternancia,
            Modalidad modalidad,
            decimal duracionHoras,
            bool esBloque,
            bool estaDividida) : base(id)
        {
            Validar(asignaturaId, docenteId, bloqueId, duracionHoras, esBloque, estaDividida);

            AsignaturaId = asignaturaId;
            DocenteId = docenteId;
            BloqueTiempoId = bloqueId;
            EspacioId = espacioId;
            GrupoId = grupoId;
            Alternancia = alternancia;
            Modalidad = modalidad;
            DuracionHoras = duracionHoras;
            EsBloque = esBloque;
            EstaDividida = estaDividida;
            Estado = EstadoSesion.Pendiente;
        }

        public void AsignarBloqueTiempo(Guid bloqueTiempoId)
        {
            if (bloqueTiempoId == Guid.Empty)
                throw new ArgumentException("El bloque de tiempo asignado no puede ser vacío.");
            
            BloqueTiempoId = bloqueTiempoId;
            Estado = EstadoSesion.Asignada;
        }

        public void MarcarConConflicto()
        {
            Estado = EstadoSesion.Conflicto;
        }

        private static void Validar(Guid asignaturaId, Guid docenteId, Guid bloqueId, decimal duracionHoras, bool esBloque, bool estaDividida)
        {
            if (asignaturaId == Guid.Empty)
                throw new ArgumentException("El ID de la asignatura no puede ser vacío.");
            if (docenteId == Guid.Empty)
                throw new ArgumentException("El ID del docente no puede ser vacío.");
            if (bloqueId == Guid.Empty)
                throw new ArgumentException("El ID del bloque de tiempo no puede ser vacío.");
            if (duracionHoras <= 0 || duracionHoras > 8)
                throw new ArgumentException("La duración debe estar entre 0 y 8 horas.");
            if (esBloque && estaDividida)
                throw new ArgumentException("Una sesión no puede ser simultáneamente bloque continuo y dividida.");
        }
    }
}
