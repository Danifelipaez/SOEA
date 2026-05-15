using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Resultado completo de la lectura del Excel de horario/currículum.
    /// Agrupa todas las entidades derivadas en un solo objeto de retorno.
    /// </summary>
    public class CurriculumExcelResult
    {
        public IReadOnlyList<Facultad> Facultades { get; }
        public IReadOnlyList<Programa> Programas { get; }
        public IReadOnlyList<Asignatura> Asignaturas { get; }
        public IReadOnlyList<Docente> Docentes { get; }
        public IReadOnlyList<Sesion> SesionesPredefinidas { get; }
        public IReadOnlyList<Espacio> Espacios { get; }

        public CurriculumExcelResult(
            IReadOnlyList<Facultad> facultades,
            IReadOnlyList<Programa> programas,
            IReadOnlyList<Asignatura> asignaturas,
            IReadOnlyList<Docente> docentes,
            IReadOnlyList<Sesion> sesionesPredefinidas,
            IReadOnlyList<Espacio> espacios)
        {
            Facultades = facultades;
            Programas = programas;
            Asignaturas = asignaturas;
            Docentes = docentes;
            SesionesPredefinidas = sesionesPredefinidas;
            Espacios = espacios;
        }
    }

    public interface ILectorExcel
    {
        /// <summary>
        /// Lee el Excel del horario existente (columnas A-J) y extrae la jerarquía completa:
        /// Facultades, Programas, Asignaturas y Docentes con su disponibilidad.
        /// </summary>
        Task<CurriculumExcelResult> LeerCurriculumAsync(Stream excelStream);

        /// <summary>
        /// Lee el Excel de Asignaturas a Programar (Modo 2).
        /// Devuelve un CurriculumExcelResult pero sin disponibilidad de bloques en los docentes.
        /// </summary>
        Task<CurriculumExcelResult> LeerAsignaturasModo2Async(Stream excelStream);

        /// <summary>
        /// Lee el Excel de Disponibilidad de Docentes (Formato 3) y actualiza/crea docentes con bloques basados en sus franjas.
        /// </summary>
        Task<IEnumerable<Docente>> LeerDisponibilidadDocentesAsync(Stream excelStream, IEnumerable<Docente> docentesExistentes);

        Task<IEnumerable<Espacio>> LeerInventarioEspaciosAsync(Stream excelStream);
    }
}
