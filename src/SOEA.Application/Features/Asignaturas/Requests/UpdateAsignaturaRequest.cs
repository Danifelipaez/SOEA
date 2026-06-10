using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class UpdateAsignaturaRequest
    {
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int HorasPorSesion { get; set; }
        public int SesionesPorSemana { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public Guid ProgramaId { get; set; }

        /// <summary>
        /// Override manual del tipo de alternancia. Si es null se infiere por el umbral
        /// de sesiones de laboratorio (misma regla que la creación).
        /// </summary>
        public TipoAlternancia? Alternancia { get; set; }

        public Guid? DocenteId { get; set; }
        public Guid? EspacioFijoId { get; set; }
    }
}
