using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Grupo de estudiantes para una asignatura en un semestre.
    /// Valida: nombre no vacío, semestre ∈ [1,10], estudiantes inscritos > 0.
    /// </summary>
    public class Grupo : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public Guid ProgramaId { get; private set; }
        public int Semestre { get; private set; }
        public int EstudiantesInscritos { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }

        // Constructor privado para EF Core
        private Grupo() : base() { }

        public Grupo(
            Guid id,
            string nombre,
            Guid programaId,
            int semestre,
            int estudiantesInscritos,
            TipoAlternancia alternancia = TipoAlternancia.SinAlternancia) : base(id)
        {
            Validar(nombre, semestre, estudiantesInscritos);

            Nombre = nombre;
            ProgramaId = programaId;
            Semestre = semestre;
            EstudiantesInscritos = estudiantesInscritos;
            Alternancia = alternancia;
        }

        /// <summary>
        /// Actualiza el nombre del grupo.
        /// </summary>
        public void ActualizarNombre(string nuevoNombre)
        {
            if (string.IsNullOrWhiteSpace(nuevoNombre))
                throw new ArgumentException("El nombre del grupo no puede estar vacío.");
            
            Nombre = nuevoNombre;
        }

        /// <summary>
        /// Actualiza la cantidad de estudiantes inscritos.
        /// </summary>
        public void ActualizarEstudiantes(int nuevaCantidad)
        {
            if (nuevaCantidad <= 0)
                throw new ArgumentException("La cantidad de estudiantes debe ser un valor positivo.");
            
            EstudiantesInscritos = nuevaCantidad;
        }

        /// <summary>
        /// Actualiza el tipo de alternancia del grupo.
        /// </summary>
        public void ActualizarAlternancia(TipoAlternancia nuevaAlternancia)
        {
            Alternancia = nuevaAlternancia;
        }

        private static void Validar(string nombre, int semestre, int estudiantesInscritos)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del grupo no puede estar vacío.");
            
            if (semestre < 1 || semestre > 10)
                throw new ArgumentException("El semestre debe estar entre 1 y 10.");
            
            if (estudiantesInscritos <= 0)
                throw new ArgumentException("La cantidad de estudiantes debe ser un valor positivo.");
        }
    }
}
