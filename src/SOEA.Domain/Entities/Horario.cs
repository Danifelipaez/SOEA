using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Horario académico para un semestre contiene un conjunto de sesiones programadas.
    /// Valida: sesiones no vacía, Hard Constraints igual a cero.
    /// </summary>
    public class Horario : EntidadBase
    {
        public string Semestre { get; private set; } = "";
        public DateTime GeneradoEn { get; private set; }
        public EstadoHorario Estado { get; private set; }
        public List<Guid> SesioneIds { get; private set; } = new();
        public int ViolacionesRestriccionesDuras { get; private set; }
        public decimal PuntajeFitness { get; private set; }

        // Constructor privado para EF Core
        private Horario() : base() { }

        public Horario(
            Guid id,
            string semestre,
            List<Guid> sesionIds,
            int violacionesRestriccionesDuras = 0,
            decimal puntajeFitness = 0m) : base(id)
        {
            Validar(semestre, sesionIds, violacionesRestriccionesDuras);

            Semestre = semestre;
            SesioneIds = sesionIds ?? new();
            ViolacionesRestriccionesDuras = violacionesRestriccionesDuras;
            PuntajeFitness = puntajeFitness;
            GeneradoEn = DateTime.UtcNow;
            Estado = EstadoHorario.Borrador;
        }

        /// <summary>
        /// Marca el horario como publicado.
        /// </summary>
        public void MarcarComoPublicado()
        {
            if (ViolacionesRestriccionesDuras > 0)
                throw new InvalidOperationException("No puede publicar un horario con violaciones de restricciones duras.");
            
            Estado = EstadoHorario.Publicado;
        }

        /// <summary>
        /// Actualiza la puntuación de restricciones blandas.
        /// </summary>
        public void ActualizarFitnessScore(decimal nuevoScore)
        {
            if (nuevoScore < 0)
                throw new ArgumentException("La puntuación de fitness no puede ser negativa.");
            
            PuntajeFitness = nuevoScore;
        }

        private static void Validar(string semestre, List<Guid> sesionIds, int violacionesRestriccionesDuras)
        {
            if (string.IsNullOrWhiteSpace(semestre))
                throw new ArgumentException("El semestre no puede estar vacío.");
            
            if (sesionIds == null || sesionIds.Count == 0)
                throw new ArgumentException("El horario debe contener al menos una sesión.");
            
            if (violacionesRestriccionesDuras < 0)
                throw new ArgumentException("Las violaciones de restricciones duras no pueden ser negativas.");
        }
    }
}