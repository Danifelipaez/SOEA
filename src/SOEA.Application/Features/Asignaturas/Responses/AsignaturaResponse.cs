using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Responses
{
    public class AsignaturaResponse
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int BloquesSemanales { get; set; }
        public bool RequiereLab { get; set; }
        public TipoAlternancia Alternancia { get; set; }
        public Guid ProgramaId { get; set; }
    }
}
