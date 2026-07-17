using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class UpdateAsignaturaRequest
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

        /// <summary>
        /// Override manual del tipo de alternancia. Si es null se infiere por el umbral
        /// de sesiones de laboratorio (misma regla que la creación).
        /// </summary>
        public TipoAlternancia? Alternancia { get; set; }

        // DocenteId se removió: el docente vive en el Grupo, no en la asignatura (ver Grupo.DocenteId).
        public Guid? EspacioFijoId { get; set; }

        /// <summary>
        /// Categoría curricular (prioridad de presencialidad, SC-PRES). Null = conservar la actual.
        /// </summary>
        public CategoriaAsignatura? Categoria { get; set; }
    }
}
