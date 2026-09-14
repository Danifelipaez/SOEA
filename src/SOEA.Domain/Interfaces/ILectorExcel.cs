using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

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
        public IReadOnlyList<Grupo> Grupos { get; }
        /// <summary>Mensajes informativos sobre filas que se omitieron o tuvieron problemas.</summary>
        public IReadOnlyList<string> Advertencias { get; }

        public CurriculumExcelResult(
            IReadOnlyList<Facultad> facultades,
            IReadOnlyList<Programa> programas,
            IReadOnlyList<Asignatura> asignaturas,
            IReadOnlyList<Docente> docentes,
            IReadOnlyList<Sesion> sesionesPredefinidas,
            IReadOnlyList<Espacio> espacios,
            IReadOnlyList<Grupo> grupos,
            IReadOnlyList<string>? advertencias = null)
        {
            Facultades = facultades;
            Programas = programas;
            Asignaturas = asignaturas;
            Docentes = docentes;
            SesionesPredefinidas = sesionesPredefinidas;
            Espacios = espacios;
            Grupos = grupos;
            Advertencias = advertencias ?? Array.Empty<string>();
        }
    }

    public interface ILectorExcel
    {
        /// <summary>
        /// Lee el Excel del horario existente (columnas A-J) y extrae la jerarquía completa.
        /// Si se proporciona catalogoBloques (mapa Dia+HoraInicio → BloqueTiempo del catálogo
        /// persistido), las sesiones predefinidas y la disponibilidad de docentes usarán esos IDs.
        /// Sin catálogo (ConsoleRunner), los bloques se crean en memoria con IDs temporales.
        /// </summary>
        Task<CurriculumExcelResult> LeerCurriculumAsync(
            Stream excelStream,
            IReadOnlyDictionary<(DiaDeSemana Dia, TimeOnly HoraInicio), BloqueTiempo>? catalogoBloques = null);
    }
}
