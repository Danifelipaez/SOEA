using System;
using System.Collections.Generic;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Docente que imparte sesiones académicas.
    /// Valida: correo único, disponibilidad horaria, máximo de horas semanales contratadas.
    /// </summary>
    public class Docente : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        /// <summary>Apellido opcional; puede estar vacío cuando el nombre completo viene en un solo campo.</summary>
        public string Apellido { get; private set; } = "";
        /// <summary>Correo institucional; opcional para el piloto (no siempre disponible en el Excel).</summary>
        public string Correo { get; private set; } = "";
        public decimal MaximoHorasSemanales { get; private set; }
        public List<FranjaHoraria> Disponibilidad { get; private set; } = new();
        /// <summary>Bloques de tiempo en los que el docente ya tiene clases (extraídos del horario existente).</summary>
        public List<BloqueTiempo> BloquesDisponibles { get; private set; } = new();
        /// <summary>Cédula de identidad del docente (dato de la UI, no requerido para el motor).</summary>
        public string? CedulaIdentidad { get; private set; }
        /// <summary>JSON crudo con la disponibilidad por día ingresada desde la UI del piloto.</summary>
        public string? DisponibilidadUiJson { get; private set; }

        // Constructor privado para EF Core
        private Docente() : base() { }

        public Docente(
            Guid id,
            string nombre,
            string apellido,
            string correo,
            decimal maximoHorasSemanales,
            List<FranjaHoraria> disponibilidad) : base(id)
        {
            Validar(nombre, apellido, correo, maximoHorasSemanales, disponibilidad);

            Nombre = nombre;
            Apellido = apellido ?? "";
            Correo = correo ?? "";
            MaximoHorasSemanales = maximoHorasSemanales;
            Disponibilidad = disponibilidad ?? new();
        }

        /// <summary>
        /// Nombre completo del docente (propiedad calculada).
        /// </summary>
        public string NombreCompleto =>
            string.IsNullOrWhiteSpace(Apellido) ? Nombre : $"{Nombre} {Apellido}";

        /// <summary>
        /// Almacena la cédula y la disponibilidad en formato JSON (datos del piloto UI).
        /// No valida formato de JSON; la UI es responsable de la consistencia.
        /// </summary>
        public void ActualizarPersistenciaUi(string? cedula, string? disponibilidadJson)
        {
            CedulaIdentidad = cedula;
            DisponibilidadUiJson = disponibilidadJson;
        }

        /// <summary>
        /// Actualiza los datos editables del docente.
        /// </summary>
        public void ActualizarDatos(string? nombre, string? apellido, string? correo, decimal maximoHorasSemanales)
        {
            Validar(nombre, apellido, correo, maximoHorasSemanales, Disponibilidad);

            Nombre = nombre!;
            Apellido = apellido ?? "";
            Correo = correo ?? "";
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
        /// Agrega un bloque de tiempo a la disponibilidad del docente (extraído del Excel del horario existente).
        /// Evita duplicados por día + hora de inicio.
        /// </summary>
        public void AgregarBloqueDisponibilidad(BloqueTiempo bloque)
        {
            if (bloque is null) throw new ArgumentNullException(nameof(bloque));
            bool duplicado = BloquesDisponibles.Exists(b => b.Dia == bloque.Dia && b.HoraInicio == bloque.HoraInicio);
            if (!duplicado)
                BloquesDisponibles.Add(bloque);
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
            // Apellido y correo son opcionales en el piloto (pueden venir vacíos cuando el nombre completo
            // llega en un solo campo desde el Excel del horario).
            if (!string.IsNullOrWhiteSpace(correo) && !EsCorreoValido(correo))
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
