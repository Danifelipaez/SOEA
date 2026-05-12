using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface ILectorExcel
    {
        Task<IEnumerable<Asignatura>> LeerCurriculumAsync(Stream excelStream);
        Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream);
        Task<IEnumerable<Espacio>> LeerInventarioEspaciosAsync(Stream excelStream);
        // Si hay otros archivos como grupos también se pueden leer,
        // pero estos son los 3 base definidos en los requerimientos.
    }
}
