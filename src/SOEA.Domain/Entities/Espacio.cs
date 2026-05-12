using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Espacio físico para impartir sesiones académicas.
    /// Valida: capacidad > 0, tipo de espacio.
    /// Sesiones virtuales: SpaceId = null, sin fila persistida en Espacio.
    /// </summary>
    public class Espacio : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public TipoEspacio Tipo { get; private set; }
        public int Capacidad { get; private set; }
        public string? Edificio { get; private set; }
        public int? Piso { get; private set; }

        // Constructor privado para EF Core
        private Espacio() : base() { }

        public Espacio(
            Guid id,
            string nombre,
            TipoEspacio tipo,
            int capacidad,
            string? edificio = null,
            int? piso = null) : base(id)
        {
            Validar(nombre, capacidad);

            Nombre = nombre;
            Tipo = tipo;
            Capacidad = capacidad;
            Edificio = edificio;
            Piso = piso;
        }

        /// <summary>
        /// Actualiza los datos editables del espacio.
        /// </summary>
        public void ActualizarDatos(string nombre, TipoEspacio tipo, string? edificio = null, int? piso = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del espacio no puede estar vacío.");

            Nombre = nombre;
            Tipo = tipo;
            Edificio = edificio;
            Piso = piso;
        }

        /// <summary>
        /// Actualiza la capacidad del espacio.
        /// </summary>
        public void ActualizarCapacidad(int nuevaCapacidad)
        {
            if (nuevaCapacidad <= 0)
                throw new ArgumentException("La capacidad debe ser un valor positivo.");
            Capacidad = nuevaCapacidad;
        }

        private static void Validar(string nombre, int capacidad)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del espacio no puede estar vacío.");
            if (capacidad <= 0)
                throw new ArgumentException("La capacidad debe ser un valor positivo.");
        }
    }
}