using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class CreateAsignaturaRequest
    {
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int HorasPorSesion { get; set; }
        public int SesionesPorSemana { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public Guid ProgramaId { get; set; }
    }
}
