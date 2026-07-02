using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Responses
{
    public class AsignaturaResponse
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public int HorasPorSesion { get; set; }
        public int SesionesPorSemana { get; set; }
        public int SesionesLaboratorioSemestre { get; set; }
        public TipoAlternancia Alternancia { get; set; }
        public Guid ProgramaId { get; set; }
        public Guid? DocenteId { get; set; }
        public Guid? EspacioFijoId { get; set; }
        public CategoriaAsignatura Categoria { get; set; }

        public static AsignaturaResponse FromEntity(Asignatura a) => new()
        {
            Id = a.Id,
            Nombre = a.Nombre,
            Codigo = a.Codigo,
            HorasPorSesion = a.HorasPorSesion,
            SesionesPorSemana = a.SesionesPorSemana,
            SesionesLaboratorioSemestre = a.SesionesLaboratorioSemestre,
            Alternancia = a.Alternancia,
            ProgramaId = a.ProgramaId,
            DocenteId = a.DocenteId,
            EspacioFijoId = a.EspacioFijoId,
            Categoria = a.Categoria
        };
    }
}
