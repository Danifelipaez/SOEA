using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class CreateAsignaturaRequest
    {
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int BloquesSemanales { get; set; }
        public bool RequiereLab { get; set; }
        public TipoAlternancia Alternancia { get; set; }
        public Guid ProgramaId { get; set; }
    }
}
