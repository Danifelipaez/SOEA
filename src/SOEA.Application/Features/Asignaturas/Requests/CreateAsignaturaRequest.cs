using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Requests
{
    public class CreateAsignaturaRequest
    {
        /// <summary>Id sugerido por el cliente (consistencia con Docentes/Espacios/Grupos). Vacío = se genera en el servidor.</summary>
        public Guid Id { get; set; }
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

        /// <summary>Override manual del tipo de alternancia. Null = se infiere por umbral.</summary>
        public TipoAlternancia? Alternancia { get; set; }

        /// <summary>Categoría curricular (prioridad de presencialidad, SC-PRES). Null = Obligatoria (default de dominio).</summary>
        public CategoriaAsignatura? Categoria { get; set; }
    }
}
