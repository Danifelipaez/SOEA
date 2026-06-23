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

        /// <summary>
        /// Docente asignado. Opcional (CR-02, modelo presencial-first): el docente deja de ser
        /// eje de generación, así que una sesión puede existir sin docente. Null = sin docente.
        /// </summary>
        public Guid? DocenteId { get; private set; }
        public Guid BloqueTiempoId { get; private set; }
        public Guid? EspacioId { get; private set; }
        public Guid? GrupoId { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public Modalidad Modalidad { get; private set; }
        public EstadoSesion Estado { get; private set; }
        public decimal DuracionHoras { get; private set; }
        public bool EsBloque { get; private set; }
        public bool EstaDividida { get; private set; }
        public string MotivoConflicto { get; private set; } = string.Empty;

        /// <summary>
        /// Flujo de la sesión (laboratorio vs aula/virtual). Cada flujo se programa por separado.
        /// Andamiaje del modelo presencial-first; el mapeo real por sesión se cablea en etapas posteriores.
        /// </summary>
        public TipoFlujo TipoFlujo { get; private set; }

        /// <summary>
        /// Patrón de alternancia aplicado (FK a <see cref="TipoAlternanciaConfig"/>).
        /// Null = presencial puro. Lo asigna el optimizador solo cuando la presión de aforo lo exige.
        /// </summary>
        public Guid? PatronAlternanciaId { get; private set; }

        /// <summary>
        /// Si es true, el optimizador no puede cambiar la alternancia de esta sesión (caso fijado).
        /// </summary>
        public bool Bloqueada { get; private set; }

        // Constructor privado para EF Core
        private Sesion() : base() { }

        public Sesion(
            Guid id,
            Guid asignaturaId,
            Guid? docenteId,
            Guid bloqueId,
            Guid? espacioId,
            Guid? grupoId,
            TipoAlternancia alternancia,
            Modalidad modalidad,
            decimal duracionHoras,
            bool esBloque,
            bool estaDividida,
            TipoFlujo tipoFlujo = TipoFlujo.Laboratorio,
            Guid? patronAlternanciaId = null,
            bool bloqueada = false) : base(id)
        {
            Validar(asignaturaId, duracionHoras, esBloque, estaDividida);

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
            TipoFlujo = tipoFlujo;
            PatronAlternanciaId = patronAlternanciaId;
            Bloqueada = bloqueada;
            Estado = EstadoSesion.Pendiente;
        }

        public void AsignarBloqueTiempo(Guid bloqueTiempoId)
        {
            if (bloqueTiempoId == Guid.Empty)
                throw new ArgumentException("El bloque de tiempo asignado no puede ser vacío.");
            
            BloqueTiempoId = bloqueTiempoId;
            Estado = EstadoSesion.Asignada;
        }

        public void AsignarEspacio(Guid espacioId)
        {
            if (espacioId == Guid.Empty)
                throw new ArgumentException("El espacio asignado no puede ser vacío.");
            
            EspacioId = espacioId;
        }

        public void MarcarConConflicto(string motivo = "")
        {
            Estado = EstadoSesion.Conflicto;
            MotivoConflicto = motivo;
        }

        /// <summary>
        /// Asigna (o desasigna con null) el docente. CR-02 (presencial-first): el docente
        /// se asigna después de generar el horario; el solape se valida en Application.
        /// </summary>
        public void AsignarDocente(Guid? docenteId)
        {
            if (docenteId == Guid.Empty)
                throw new ArgumentException("El docente asignado no puede ser un Guid vacío (use null para desasignar).");
            DocenteId = docenteId;
        }

        /// <summary>
        /// SC-PRES: convierte una sesión presencial a virtual cuando la capacidad de espacios
        /// está saturada. Se aplica en orden de prioridad (Electiva → Optativa → Obligatoria)
        /// antes de pasar el problema a CP-SAT. EspacioId pasa a null.
        /// </summary>
        public void VirtualizarSesion()
        {
            Modalidad = Modalidad.Virtual;
            EspacioId = null;
        }

        public void EstablecerFlujo(TipoFlujo tipoFlujo)
        {
            TipoFlujo = tipoFlujo;
        }

        public void EstablecerPatronAlternancia(Guid? patronAlternanciaId)
        {
            PatronAlternanciaId = patronAlternanciaId;
        }

        public void Bloquear()
        {
            Bloqueada = true;
        }

        public void Desbloquear()
        {
            Bloqueada = false;
        }

        private static void Validar(Guid asignaturaId, decimal duracionHoras, bool esBloque, bool estaDividida)
        {
            if (asignaturaId == Guid.Empty)
                throw new ArgumentException("El ID de la asignatura no puede ser vacío.");
            // CR-02: el docente es opcional — no se valida su presencia.
            if (duracionHoras <= 0 || duracionHoras > 8)
                throw new ArgumentException("La duración debe estar entre 0 y 8 horas.");
            if (esBloque && estaDividida)
                throw new ArgumentException("Una sesión no puede ser simultáneamente bloque continuo y dividida.");
        }
    }
}
