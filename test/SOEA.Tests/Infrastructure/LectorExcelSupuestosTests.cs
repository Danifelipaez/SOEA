using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Excel;
using Xunit;

namespace SOEA.Tests.Infrastructure
{
    /// <summary>
    /// DB-10 (auditoría 2026-09-28): el formato de Excel no trae capacidad de espacio, estudiantes por grupo ni horas
    /// del docente, y el lector los rellena. CLAUDE.md §4 prohíbe inventarlos; al menos se avisa por nombre.
    /// NEW-6: una fila sin docente ya no se descarta.
    /// </summary>
    public class LectorExcelSupuestosTests
    {
        private static MemoryStream Excel(params object[][] filas)
        {
            ExcelPackage.License.SetNonCommercialPersonal("SOEA");
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            object[] cabecera = { "Facultad", "Programa", "Asignatura", "Grupo", "Docente", "Reales [h]", "Espacio", "Dia", "Hora", "Final" };
            for (var c = 0; c < cabecera.Length; c++) hoja.Cells[1, c + 1].Value = cabecera[c];
            for (var f = 0; f < filas.Length; f++)
                for (var c = 0; c < cabecera.Length; c++) hoja.Cells[f + 2, c + 1].Value = filas[f][c];
            return new MemoryStream(paquete.GetAsByteArray());
        }

        private static object[] Fila(string asignatura, string docente, string espacio) =>
            new object[] { "FAC", "PROG", asignatura, 1, docente, 2, espacio, "Lunes", "08:00", "10:00" };

        private static Task<CurriculumExcelResult> Leer(MemoryStream s) =>
            new LectorExcel(NullLogger<LectorExcel>.Instance).LeerCurriculumAsync(s);

        [Fact]
        public async Task AvisaPorNombreDeLosValoresQueAsume_ConLaMuestraAcotada()
        {
            var filas = Enumerable.Range(1, 7).Select(i => Fila($"ASIG {i}", $"DOC {i}", $"AULA {i}")).ToArray();

            var r = await Leer(Excel(filas));

            var espacios = Assert.Single(r.Advertencias, a => a.Contains("capacidad 30 supuesta"));
            Assert.StartsWith("7 espacio(s)", espacios);
            Assert.Contains("AULA 1", espacios);
            Assert.Contains("y 2 más", espacios);   // solo se muestran 5 nombres
            Assert.DoesNotContain("AULA 7", espacios);
            Assert.Single(r.Advertencias, a => a.StartsWith("7 grupo(s)") && a.Contains("30 estudiantes supuestos"));
            Assert.Single(r.Advertencias, a => a.StartsWith("7 docente(s)") && a.Contains("40 horas semanales"));
        }

        [Fact]
        public async Task SinFilasNuevas_NoInventaAvisos()
        {
            var r = await Leer(Excel());   // solo cabecera

            Assert.DoesNotContain(r.Advertencias, a => a.Contains("supuest"));
        }

        [Fact]
        public async Task FilaSinDocente_CreaElGrupoSinDocente_YAvisa()
        {
            var r = await Leer(Excel(Fila("CON DOCENTE", "DOC", "AULA A"), Fila("SIN DOCENTE", "", "AULA B")));

            Assert.Equal(2, r.Grupos.Count);                                   // NEW-6: antes 1
            Assert.Single(r.Grupos, g => g.DocenteId is null);
            Assert.Contains(r.Advertencias, a => a.Contains("SIN DOCENTE") && a.Contains("sin docente"));
            Assert.Single(r.Docentes);                                          // el "docente vacío" no se inventa
        }

        [Fact]
        public async Task DuracionMayorQue8_FallaConLaFilaNombrada_NoConArchivoDanado()
        {
            var fila = new object[] { "FAC", "PROG", "ASIG", 1, "DOC", 99, "AULA", "Lunes", "08:00", "10:00" };

            var ex = await Assert.ThrowsAsync<SOEA.Domain.Exceptions.ArchivoImportacionInvalidoException>(() => Leer(Excel(fila)));

            Assert.Contains("Fila 2", ex.Message);
            Assert.Contains("99", ex.Message);
        }

        [Fact]
        public async Task DuracionNoNumerica_AvisaYUsa2Horas()
        {
            var fila = new object[] { "FAC", "PROG", "ASIG", 1, "DOC", "dos", "AULA", "Lunes", "08:00", "10:00" };

            var r = await Leer(Excel(fila));

            Assert.Contains(r.Advertencias, a => a.Contains("Fila 2") && a.Contains("'dos'") && a.Contains("2 h"));
        }

        [Fact]
        public async Task SinColumnaAsignaturaEnLaCabecera_AvisaQueLeyoPorPosicion()
        {
            ExcelPackage.License.SetNonCommercialPersonal("SOEA");
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            var cabecera = new[] { "Facultad", "Programa", "Materia", "Docente" };   // "Materia" no se reconoce
            for (var c = 0; c < cabecera.Length; c++) hoja.Cells[1, c + 1].Value = cabecera[c];
            hoja.Cells[2, 1].Value = "FAC"; hoja.Cells[2, 2].Value = "PROG"; hoja.Cells[2, 3].Value = "Cálculo"; hoja.Cells[2, 4].Value = "DOC";

            var r = await Leer(new MemoryStream(paquete.GetAsByteArray()));

            Assert.Contains(r.Advertencias, a => a.Contains("'Asignatura'") && a.Contains("columna 3"));
            Assert.DoesNotContain(r.Advertencias, a => a.Contains("'Facultad'") || a.Contains("'Programa'"));
        }

        [Fact]
        public async Task TextoMasLargoQueLaColumna_FallaNombrandoFilaYColumna()
        {
            var fila = new object[] { "FAC", "PROG", "ASIG", 1, new string('D', 101), 2, "AULA", "Lunes", "08:00", "10:00" };

            var ex = await Assert.ThrowsAsync<SOEA.Domain.Exceptions.ArchivoImportacionInvalidoException>(() => Leer(Excel(fila)));

            Assert.Contains("Fila 2", ex.Message);
            Assert.Contains("'Docente'", ex.Message);
            Assert.Contains("100", ex.Message);
        }

        [Fact]
        public async Task NombreDeGrupoDerivadoMasLargoQue100_SeAcortaYAvisa()
        {
            // 95 caracteres pasan el límite de la asignatura (255), pero "{asignatura} - Grupo 1" no cabe en Grupos.nombre (100):
            // se acorta la parte de la asignatura en vez de rechazar todo el archivo.
            var fila = new object[] { "FAC", "PROG", new string('A', 95), 1, "DOC", 2, "AULA", "Lunes", "08:00", "10:00" };

            var r = await Leer(Excel(fila));

            var grupo = Assert.Single(r.Grupos);
            Assert.Equal(100, grupo.Nombre.Length);
            Assert.EndsWith(" - Grupo 1", grupo.Nombre);
            Assert.Contains(r.Advertencias, a => a.Contains("Fila 2") && a.Contains("se acortó"));
        }

        [Fact]
        public async Task SinDocenteNiGrupo_FilasALaMismaHoraSonSeccionesDistintas()
        {
            object[] F(string dia) => new object[] { "FAC", "PROG", "CALCULO", null!, "", 2, "AULA", dia, "08:00", "10:00" };

            var r = await Leer(Excel(F("Lunes"), F("Lunes"), F("Miercoles")));

            // Dos clases el lunes a la misma hora no pueden ser del mismo grupo; la del miércoles se une al primero.
            Assert.Equal(2, r.Grupos.Count);
        }

        [Theory]
        [InlineData("10:00")]   // Final = Hora
        [InlineData("09:00")]   // Final anterior a Hora
        public async Task FinalNoPosteriorAHora_AvisaYDejaUnaDisponibilidadValida(string final)
        {
            var fila = new object[] { "FAC", "PROG", "ASIG", 1, "DOC", 2, "AULA", "Lunes", "10:00", final };

            var r = await Leer(Excel(fila));

            Assert.Contains(r.Advertencias, a => a.Contains("Fila 2") && a.Contains("no es posterior"));
            var grupo = Assert.Single(r.Grupos);
            Assert.True(SOEA.Domain.ValueObjects.DisponibilidadSemanal.JsonEsValido(grupo.DisponibilidadUiJson));
            Assert.Contains("12:00", grupo.DisponibilidadUiJson);   // Hora + 2 h
        }
    }
}
