using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    public class Docente
    {
        public Guid Id { get; private set; }
        public string Nombre { get; private set; } ="";
        public string Apellido { get; private set; } ="";
        public string Correo { get; private set; } ="";
        public int MaximoHorasSemanales { get; private set; }
        public List<FranjaHoraria> Disponibilidad { get; private set; } = new();
        


    }
}