using SOEA.Domain.Enums;

namespace SOEA.Domain.ValueObjects
{
    public class IntervaloTiempo
    {
        // Constantes de restricción de tiempo
        private static readonly TimeOnly EmpiezaHraLabor = new(6, 0);      // 06:00
        private static readonly TimeOnly TerminaHraLabor = new(22, 00);      // 22:00

        public Guid Id { get; private set; }
        public DiaDeSemana Dia { get; private set; }
        public TimeOnly HoraInicio { get; private set; }
        public TimeOnly HoraFin { get; private set; }
        private IntervaloTiempo() { } //EF Core
        
        
        public IntervaloTiempo(Guid id, DiaDeSemana dia, TimeOnly horaInicio, TimeOnly horaFin)
        {
            // HC-T01: Las sesiones deben estar dentro del horario de operación
            if (horaInicio < EmpiezaHraLabor)
                throw new ArgumentException(
                    $"La hora de inicio ({horaInicio}) no puede ser anterior a {EmpiezaHraLabor}. (HC-T01)",
                    nameof(horaInicio));

            if (horaFin > TerminaHraLabor)
                throw new ArgumentException(
                    $"La hora de fin ({horaFin}) no puede ser posterior a {TerminaHraLabor}. (HC-T01)",
                    nameof(horaFin));

            // Validación base: HoraFin > horaInicio
            if (horaFin <= horaInicio)
                throw new ArgumentException(
                    $"La hora de fin ({horaFin}) debe ser posterior a la hora de inicio ({horaInicio}).",
                    nameof(horaFin));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            Dia = dia;
            HoraInicio = horaInicio;
            HoraFin = horaFin;
        }
        public decimal GetDuracionEnHoras()
        {
            var duracion = HoraFin - HoraInicio;
            return (decimal)duracion.TotalHours;
        }
        public bool SuperponeCon(IntervaloTiempo otraFranja)
        {
            if (otraFranja == null) throw new ArgumentNullException(nameof(otraFranja));
            if (this.Dia != otraFranja.Dia) return false; // Solo se superponen si son el mismo día

            // Se superponen si el inicio de una franja está entre el inicio y fin de la otra
            return (this.HoraInicio < otraFranja.HoraFin && this.HoraFin > otraFranja.HoraInicio);

        }

        public bool EsValida()
        {
            // HC-T01: Las sesiones deben estar dentro del horario de operación
            if (HoraInicio < EmpiezaHraLabor || HoraFin > TerminaHraLabor)
                return false;

            // Validación base: HoraFin > horaInicio
            if (HoraFin <= HoraInicio)
                return false;

            return true;
        }
        
        public override bool Equals(object? obj)
        {
            if (obj is not IntervaloTiempo other)
                return false;

            return this.Id == other.Id
                && this.Dia == other.Dia
                && this.HoraInicio == other.HoraInicio
                && this.HoraFin == other.HoraFin;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Dia, HoraInicio, HoraFin);
        }

        public override string ToString()
        {
            return $"{Dia} {HoraInicio:hh\\:mm}–{HoraFin:hh\\:mm} ({GetDuracionEnHoras()}h)";
        }
    }
    
}
