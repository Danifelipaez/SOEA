using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    public class Sesion
    {
        public Guid Id { get; private set; }
        public Guid IdAsignatura { get; private set; }
        public Guid IdEspacio { get; private set; }
        public Guid IdDocente { get; private set; }
        public Guid IdGrupo { get; private set; }
        public Guid FranjadeTiempo { get; private set; }
        public TipoAlternancia Alternancia { get; private set; }
        public Modalidad Modalidad { get; private set; }
        public EstadoSesion Estado { get; private set; }
        public int DuracionHoras { get; private set; }


        public Sesion()
        {
        }

        
    }         
}