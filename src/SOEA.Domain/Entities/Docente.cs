using System;
using System.Collections.Generic;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Docente que imparte sesiones académicas.
    /// Valida: correo único, disponibilidad horaria, máximo de horas semanales contratadas.
    /// </summary>
    public class Docente : BaseEntity
    {
        public string Nombre { get; private set; } = "";
        public string Apellido { get; private set; } = "";
        public string Correo { get; private set; } = "";
        public decimal MaximoHorasSemanales { get; private set; }
        public List<FranjaHoraria> Disponibilidad { get; private set; } = new();

        // Constructor privado para EF Core
        private Docente() : base() { }

        public Docente(
            Guid id,
            string? nombre,
            string? apellido,
            string? correo,
            decimal maximoHorasSemanales,
            List<FranjaHoraria> disponibilidad) : base(id)
        {
            Validar(nombre, apellido, correo, maximoHorasSemanales, disponibilidad);

            Nombre = nombre;
            Apellido = apellido;
            Correo = correo;
            MaximoHorasSemanales = maximoHorasSemanales;
            Disponibilidad = disponibilidad ?? new();
        }

        /// <summary>
        /// Nombre completo del docente (propiedad calculada).
        /// </summary>
        public string NombreCompleto => $"{Nombre} {Apellido}";

        /// <summary>
        /// Actualiza los datos editables del docente.
        /// </summary>
        public void ActualizarDatos(string? nombre, string? apellido, string? correo, decimal maximoHorasSemanales)
        {
            Validar(nombre, apellido, correo, maximoHorasSemanales, Disponibilidad);

            Nombre = nombre;
            Apellido = apellido;
            Correo = correo;
            MaximoHorasSemanales = maximoHorasSemanales;
        }

        /// <summary>
        /// Actualiza la disponibilidad horaria del docente.
        /// </summary>
        public void ActualizarDisponibilidad(List<FranjaHoraria> nuevaDisponibilidad)
        {
            if (nuevaDisponibilidad is null || nuevaDisponibilidad.Count == 0)
                throw new ArgumentException("La disponibilidad no puede estar vacía.");
            Disponibilidad = new List<FranjaHoraria>(nuevaDisponibilidad);
        }

        /// <summary>
        /// Actualiza el máximo de horas semanales contratadas.
        /// </summary>
        public void ActualizarMaximoHoras(decimal maxHoras)
        {
            if (maxHoras <= 0)
                throw new ArgumentException("El máximo de horas semanales debe ser un valor positivo.");
            MaximoHorasSemanales = maxHoras;
        }

        private static void Validar(string? nombre, string? apellido, string? correo, decimal maximoHoras, List<FranjaHoraria>? disponibilidad)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del docente no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(apellido))
                throw new ArgumentException("El apellido del docente no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(correo))
                throw new ArgumentException("El correo del docente no puede estar vacío.");
            if (!EsCorreoValido(correo))
                throw new ArgumentException("El correo no tiene un formato válido.");
            if (maximoHoras <= 0)
                throw new ArgumentException("El máximo de horas semanales debe ser un valor positivo.");
            if (disponibilidad is null || disponibilidad.Count == 0)
                throw new ArgumentException("La disponibilidad no puede estar vacía.");
        }

        private static bool EsCorreoValido(string correo)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(correo);
                return addr.Address == correo;
            }
            catch
            {
                return false;
            }
        }
    }
}