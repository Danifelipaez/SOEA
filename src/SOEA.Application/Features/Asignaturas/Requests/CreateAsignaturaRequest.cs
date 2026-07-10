using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class CreateAsignaturaRequest
    {
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int SesionesTeoriaPresencialSemana { get; set; }
        public int HorasTeoriaPresencial { get; set; }
        public int SesionesTeoriaVirtualSemana { get; set; }
        public int HorasTeoriaVirtual { get; set; }
        public int SesionesLaboratorioSemana { get; set; }
        public int HorasLaboratorio { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public Guid ProgramaId { get; set; }
    }
}
