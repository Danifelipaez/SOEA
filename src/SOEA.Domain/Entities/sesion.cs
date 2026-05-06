namespace SOEA.Domain.Entities

{
    public class Sesion
    {
        public int Id { get; private set; }
        public int IdAsignatura { get; private set; }
        public int? IdEspacio { get; private set; }
        public DiaDeSemana Dia { get; private set; }
        public TimeSpan HoraInicio { get; private set; }
        public TimeSpan HoraFin { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public string Modalidad { get; private set; } = string.Empty;

        public Sesion()
        {
        }

        public Sesion(
            int idAsignatura,
            int? idEspacio,
            DiaDeSemana dia,
            TimeSpan horaInicio,
            TimeSpan horaFin,
            TipoAlternancia alternancia,
            string modalidad)
        {
            if (horaFin <= horaInicio)
            {
                throw new ArgumentException("La hora de fin debe ser mayor que la hora de inicio.", nameof(horaFin));
            }

            if (string.IsNullOrWhiteSpace(modalidad))
            {
                throw new ArgumentException("La modalidad es obligatoria.", nameof(modalidad));
            }

            IdAsignatura = idAsignatura;
            IdEspacio = idEspacio;
            Dia = dia;
            HoraInicio = horaInicio;
            HoraFin = horaFin;
            Alternancia = alternancia;
            Modalidad = modalidad;
        }

        public void AsignarEspacio(int? idEspacio)
        {
            IdEspacio = idEspacio;
        }

    }
}