using SOEA.Domain.Enums;
namespace SOEA.Domain.Entities
{
    public class Espacio
    {
        public Guid Id { get; private set; }
        public string Nombre { get; private set; } ="";
        public TipoEspacio Tipo { get; private set; }
        public int Capacidad { get; private set; }
        public string Ubicacion { get; private set; } ="";
        public int piso { get; private set; }

        public Espacio()
        {
        }
    }
}