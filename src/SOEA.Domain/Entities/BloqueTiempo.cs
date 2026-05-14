using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Bloque discreto de tiempo programable para sesiones académicas.
    /// Representa un intervalo de tiempo en un día específico (ej: lunes 09:00-11:00).
    /// Validado: horaInicio < horaFin, ambas dentro de rango institucional [07:00-21:30].
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
            Validar(horaInicio, horaFin);

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

        private static void Validar(TimeOnly horaInicio, TimeOnly horaFin)
        {
            var minHora = new TimeOnly(6, 00);
            var maxHora = new TimeOnly(22, 00);

            if (horaInicio < minHora)
                throw new ArgumentException("La hora de inicio debe ser >= 06:00.");
            if (horaFin > maxHora)
                throw new ArgumentException("La hora de fin debe ser <= 22:00.");
            if (horaInicio >= horaFin)
                throw new ArgumentException("La hora de inicio debe ser menor que la hora de fin.");
        }
    }
}
