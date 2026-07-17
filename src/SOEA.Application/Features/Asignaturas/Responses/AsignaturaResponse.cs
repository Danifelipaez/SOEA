using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Asignaturas.Responses
{
    public class AsignaturaResponse
    {
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
        public TipoAlternancia Alternancia { get; set; }
        public Guid ProgramaId { get; set; }
        public Guid? EspacioFijoId { get; set; }
        public CategoriaAsignatura Categoria { get; set; }

        public static AsignaturaResponse FromEntity(Asignatura a) => new()
        {
            Id = a.Id,
            Nombre = a.Nombre,
            Codigo = a.Codigo,
            SesionesTeoriaPresencialSemana = a.SesionesTeoriaPresencialSemana,
            HorasTeoriaPresencial = a.HorasTeoriaPresencial,
            SesionesTeoriaVirtualSemana = a.SesionesTeoriaVirtualSemana,
            HorasTeoriaVirtual = a.HorasTeoriaVirtual,
            SesionesLaboratorioSemana = a.SesionesLaboratorioSemana,
            HorasLaboratorio = a.HorasLaboratorio,
            SesionesLaboratorioSemestre = a.SesionesLaboratorioSemestre,
            Alternancia = a.Alternancia,
            ProgramaId = a.ProgramaId,
            EspacioFijoId = a.EspacioFijoId,
            Categoria = a.Categoria
        };
    }
}
