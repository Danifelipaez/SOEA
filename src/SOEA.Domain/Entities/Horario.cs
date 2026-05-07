using SOEA.Domain.Enums;
namespace SOEA.Domain.Entities
{
    public class Horario
    {
        public Guid Id { get; private set; }
        public string Semestre { get; private set; } ="";
        public DateTime GeneratedAt { get; private set; }
        public EstadoHorario Estado { get; private set; }
        public List<Sesion> Sesiones { get; private set; } = new();

        public Horario(List<Sesion> sesiones)
        {
            Sesiones = sesiones;
        }

        public int HardConstraintViolations { get; private set; }
        public decimal SoftConstraintFitnessScore { get; private set; }
    }
}