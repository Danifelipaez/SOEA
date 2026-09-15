using System;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Bloque discreto de tiempo programable para sesiones académicas.
    /// Representa un intervalo de tiempo en un día específico (ej: lunes 09:00-11:00).
    /// Validado contra el rango institucional único de <see cref="GrillaInstitucional"/>.
    /// </summary>
    public class BloqueTiempo : EntidadBase
    {
        public DiaDeSemana Dia { get; private set; }
        public TimeOnly HoraInicio { get; private set; }
        public TimeOnly HoraFin { get; private set; }

        // Constructor privado para EF Core
        private BloqueTiempo() : base() { }

        public BloqueTiempo(
            Guid id,
            DiaDeSemana dia,
            TimeOnly horaInicio,
            TimeOnly horaFin) : base(id)
        {
            Validar(dia, horaInicio, horaFin);

            Dia = dia;
            HoraInicio = horaInicio;
            HoraFin = horaFin;
        }

        /// <summary>
        /// Duración del bloque en horas (propiedad calculada).
        /// </summary>
        public decimal Duracion
        {
            get
            {
                var diff = HoraFin.ToTimeSpan() - HoraInicio.ToTimeSpan();
                return (decimal)diff.TotalHours;
            }
        }

        private static void Validar(DiaDeSemana dia, TimeOnly horaInicio, TimeOnly horaFin)
        {
            // Fuente única del rango institucional: GrillaInstitucional (H1 auditoría). Antes este
            // método tenía sus propios literales (06:00–22:00 / 06:00–14:00) que ya coincidían por
            // casualidad con la grilla actual, pero un tercer valor (06:00–13:00) vivía en
            // GrillaInstitucional.HoraCierreSabado — la divergencia es lo que dejaba fabricar
            // bloques fantasma en el import.
            var minHora = dia == DiaDeSemana.Sábado
                ? GrillaInstitucional.HoraAperturaSabado
                : GrillaInstitucional.HoraAperturaLunesAViernes;
            var maxHora = dia == DiaDeSemana.Sábado
                ? GrillaInstitucional.HoraCierreSabado
                : GrillaInstitucional.HoraCierreLunesAViernes;

            if (horaInicio < minHora)
                throw new ArgumentException($"La hora de inicio debe ser >= {minHora:HH\\:mm}.");
            if (horaFin > maxHora)
                throw new ArgumentException($"La hora de fin debe ser <= {maxHora:HH\\:mm} para el día {dia}.");
            if (horaInicio >= horaFin)
                throw new ArgumentException("La hora de inicio debe ser menor que la hora de fin.");
        }
    }
}
