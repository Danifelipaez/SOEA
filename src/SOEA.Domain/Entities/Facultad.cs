using System;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Representa una facultad dentro de la universidad.
    /// </summary>
    public class Facultad : EntidadBase
    {
        public string Nombre { get; private set; } = "";

        // Constructor privado para EF Core
        private Facultad() : base() { }

        public Facultad(Guid id, string nombre) : base(id)
        {
            Validar(nombre);
            Nombre = nombre;
        }

        public void ActualizarNombre(string nombre)
        {
            Validar(nombre);
            Nombre = nombre;
        }

        private static void Validar(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre de la facultad no puede estar vacío.");
        }
    }
}
