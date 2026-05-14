using System;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Representa un programa académico (ej. Biología, Ingeniería Química).
    /// </summary>
    public class Programa : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public Guid FacultadId { get; private set; }

        // Constructor privado para EF Core
        private Programa() : base() { }

        public Programa(Guid id, string nombre, Guid facultadId) : base(id)
        {
            Validar(nombre, facultadId);
            
            Nombre = nombre;
            FacultadId = facultadId;
        }

        public void ActualizarDatos(string nombre, Guid facultadId)
        {
            Validar(nombre, facultadId);
            
            Nombre = nombre;
            FacultadId = facultadId;
        }

        private static void Validar(string nombre, Guid facultadId)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del programa no puede estar vacío.");
            if (facultadId == Guid.Empty)
                throw new ArgumentException("El ID de la facultad no puede ser vacío.");
        }
    }
}
