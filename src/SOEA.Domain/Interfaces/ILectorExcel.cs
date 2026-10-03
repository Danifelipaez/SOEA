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

    /// <summary>
    /// Una fila del Excel tal como viene (texto crudo), con su número de fila real. Es lo que el
    /// operador revisa y corrige antes de importar — por eso viaja como texto y no como entidades.
    /// </summary>
    public sealed record FilaCurriculum(
        int Fila, string? Facultad, string? Programa, string? Asignatura, string? Codigo,
        string? TipoEspacio, string? Espacio, string? Duracion, string? Dia, string? Hora,
        string? Final, string? Docente, string? Grupo);

    /// <summary>Filas de la hoja + avisos sobre la cabecera (columnas no reconocidas).</summary>
    public sealed record HojaCurriculum(IReadOnlyList<FilaCurriculum> Filas, IReadOnlyList<string> Avisos);

    /// <summary>
    /// Problema de una fila. Campo = nombre de la propiedad de <see cref="FilaCurriculum"/> en
    /// minúscula ("duracion", "final"…) o "" si afecta a la fila entera. EsError = impide importar
    /// hasta corregir o borrar la fila; si no, es un aviso que el operador puede aceptar.
    /// </summary>
    public sealed record IncoherenciaFila(int Fila, string Campo, bool EsError, string Mensaje);

    public interface ILectorExcel
    {
        /// <summary>Lee las filas de la primera hoja sin interpretarlas (columnas por cabecera).</summary>
        Task<HojaCurriculum> LeerFilasAsync(Stream excelStream);

        /// <summary>Incoherencias de las filas, por fila y entre filas. No modifica nada.</summary>
        IReadOnlyList<IncoherenciaFila> ValidarFilas(IReadOnlyList<FilaCurriculum> filas);

        /// <summary>
        /// Construye la jerarquía completa a partir de las filas (ya revisadas).
        /// Si se proporciona catalogoBloques (mapa Dia+HoraInicio → BloqueTiempo del catálogo
        /// persistido), las sesiones predefinidas usarán esos IDs; sin catálogo, los bloques se
        /// crean en memoria con IDs temporales.
        /// </summary>
        CurriculumExcelResult ConstruirCurriculum(
            IReadOnlyList<FilaCurriculum> filas,
            IReadOnlyDictionary<(DiaDeSemana Dia, TimeOnly HoraInicio), BloqueTiempo>? catalogoBloques = null);
    }
}
