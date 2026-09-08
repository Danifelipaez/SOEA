using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SOEA.Domain.Enums;
using SOEA.Infrastructure.Excel;
using Xunit;

namespace SOEA.Tests.Infrastructure.Excel
{
    /// <summary>
    /// Formato real entregado por Rosa (coordinación académica): columnas
    /// FACULTAD | PROGRAMA | ASIGNATURA | GRUPO | DOCENTE | REALES [h] | ESPACIO | DIA | HORA | FINAL.
    /// Difiere del formato legado (Código/TipoEspacio/Docente al final, sin Grupo ni hora de Final
    /// explícita) — este test fija el comportamiento esperado tras adaptar <see cref="LectorExcel"/>
    /// a las cabeceras reales en vez de posiciones fijas.
    /// </summary>
    public class LectorExcelFormatoRosaTests
    {
        static LectorExcelFormatoRosaTests()
        {
            ExcelPackage.License.SetNonCommercialPersonal("SOEA Tests");
        }

        private static Stream ConstruirExcelFormatoRosa()
        {
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            string[] cabeceras = { "FACULTAD", "PROGRAMA", "ASIGNATURA", "GRUPO", "DOCENTE", "REALES [h]", "ESPACIO", "DIA", "HORA", "FINAL" };
            for (int c = 0; c < cabeceras.Length; c++) hoja.Cells[1, c + 1].Value = cabeceras[c];

            void Fila(int fila, string facultad, string programa, string asignatura, string grupo, string docente,
                string horas, string espacio, string dia, string hora, string final)
            {
                hoja.Cells[fila, 1].Value = facultad;
                hoja.Cells[fila, 2].Value = programa;
                hoja.Cells[fila, 3].Value = asignatura;
                hoja.Cells[fila, 4].Value = grupo;
                hoja.Cells[fila, 5].Value = docente;
                hoja.Cells[fila, 6].Value = horas;
                hoja.Cells[fila, 7].Value = espacio;
                hoja.Cells[fila, 8].Value = dia;
                hoja.Cells[fila, 9].Value = hora;
                hoja.Cells[fila, 10].Value = final;
            }

            Fila(2, "INGENIERIA", "FACULTAD DE INGENIERIA", "QUIMICA GENERAL", "7", "Jose Bustamante Bolaños", "2", "QUI", "LUNES", "6:00", "8:00");
            Fila(3, "INGENIERIA", "FACULTAD DE INGENIERIA", "QUIMICA GENERAL", "10", "Jose Bustamante Bolaños", "2", "QUI", "LUNES", "8:00", "10:00");
            Fila(4, "BASICAS", "QUIMICA", "INTRODUCCION QUIMICA", "1", "Jeniffer Garcia Beleño", "3", "QUI", "MARTES", "18:00", "21:00");
            // REALES [h] deliberadamente inconsistente con FINAL (diría 14:00-16:00):
            // FINAL debe ganar y producir 3 bloques de disponibilidad (14-17), no 2 (14-16).
            Fila(5, "INGENIERIA", "FACULTAD DE INGENIERIA", "QUIMICA GENERAL", "20", "Luis Borja Hidalgo", "2", "QUI", "MIERCOLES", "14:00", "17:00");

            var stream = new MemoryStream();
            paquete.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        [Fact]
        public async Task LeerCurriculumAsync_FormatoRosa_MapeaCadaColumnaPorSuNombreReal()
        {
            var lector = new LectorExcel(NullLogger<LectorExcel>.Instance);
            using var stream = ConstruirExcelFormatoRosa();

            var resultado = await lector.LeerCurriculumAsync(stream);

            // GRUPO: número real preservado (no un contador auto-incremental interno).
            var grupo7 = Assert.Single(resultado.Grupos, g => g.Nombre.Contains("Grupo 7"));
            Assert.Equal("7", grupo7.Codigo);
            var grupo10 = Assert.Single(resultado.Grupos, g => g.Nombre.Contains("Grupo 10"));
            Assert.Equal("10", grupo10.Codigo);

            // ESPACIO ("QUI"): debe leerse como nombre específico del espacio, no como su tipo.
            var espacio = Assert.Single(resultado.Espacios);
            Assert.Equal("QUI", espacio.Nombre);
            Assert.Equal(TipoEspacio.Salon, espacio.Tipo);

            // REALES [h]: duración explícita por fila, incluida la fila de 3 horas.
            var quimicaGeneral = resultado.Asignaturas.Single(a => a.Nombre == "QUIMICA GENERAL");
            Assert.Equal(2, quimicaGeneral.HorasPorSesion);
            var introQuimica = resultado.Asignaturas.Single(a => a.Nombre == "INTRODUCCION QUIMICA");
            Assert.Equal(3, introQuimica.HorasPorSesion);

            // FINAL: hora de fin explícita autoritativa (18:00-21:00 = 3h, no 18:00+2h por defecto).
            var sesionIntro = resultado.SesionesPredefinidas.Single(s => s.AsignaturaId == introQuimica.Id);
            Assert.Equal(3, sesionIntro.DuracionHoras);

            // FINAL contradice a REALES [h] (14:00-17:00 = 3h reales vs. "2" declarado): FINAL manda
            // sobre la disponibilidad real del docente, que debe cubrir las 3 horas, no 2.
            var docenteLuis = resultado.Docentes.Single(d => d.Nombre == "Luis Borja Hidalgo");
            Assert.Equal(3, docenteLuis.BloquesDisponibles.Count);
            Assert.Contains(docenteLuis.BloquesDisponibles, b => b.HoraInicio == new TimeOnly(16, 0) && b.HoraFin == new TimeOnly(17, 0));

            Assert.Empty(resultado.Advertencias);
        }

        [Fact]
        public async Task LeerCurriculumAsync_FormatoRosa_SinColumnaCodigo_NoConfundeElNumeroDeGrupoConElCodigoDeAsignatura()
        {
            // Regresión: sin cabecera "Código", el fallback posicional caía en la columna 4, que en
            // este formato es "Grupo" — dos asignaturas del MISMO programa con el mismo número de
            // grupo (ambas "Grupo 1", aquí) terminaban con el mismo Asignatura.Codigo="1" y violaban
            // el índice único (codigo, programa_id) al persistir.
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            string[] cabeceras = { "FACULTAD", "PROGRAMA", "ASIGNATURA", "GRUPO", "DOCENTE", "REALES [h]", "ESPACIO", "DIA", "HORA", "FINAL" };
            for (int c = 0; c < cabeceras.Length; c++) hoja.Cells[1, c + 1].Value = cabeceras[c];

            void Fila(int fila, string asignatura, string grupo, string docente)
            {
                hoja.Cells[fila, 1].Value = "BASICAS";
                hoja.Cells[fila, 2].Value = "QUIMICA";
                hoja.Cells[fila, 3].Value = asignatura;
                hoja.Cells[fila, 4].Value = grupo;
                hoja.Cells[fila, 5].Value = docente;
                hoja.Cells[fila, 6].Value = "2";
                hoja.Cells[fila, 7].Value = "QUI";
                hoja.Cells[fila, 8].Value = "LUNES";
                hoja.Cells[fila, 9].Value = "6:00";
                hoja.Cells[fila, 10].Value = "8:00";
            }
            Fila(2, "QUIMICA GENERAL", "1", "Docente A");
            Fila(3, "BIOQUIMICA", "1", "Docente B");

            var stream = new MemoryStream();
            paquete.SaveAs(stream);
            stream.Position = 0;

            var lector = new LectorExcel(NullLogger<LectorExcel>.Instance);
            var resultado = await lector.LeerCurriculumAsync(stream);

            Assert.Equal(2, resultado.Asignaturas.Count);
            var codigos = resultado.Asignaturas.Select(a => a.Codigo).ToList();
            Assert.NotEqual(codigos[0], codigos[1]);
            Assert.DoesNotContain(codigos, c => c == "1");
        }

        [Fact]
        public async Task LeerCurriculumAsync_UnDocenteConVariasSecciones_NoInflaSesionesPorSemanaDeLaAsignatura()
        {
            // Regresión real (prueba.xlsx, "QUIMICA ORGANICA" / FACULTAD DE INGENIERIA): un mismo
            // docente dicta 4 GRUPOS distintos de la misma asignatura, cada uno reuniéndose una
            // sola vez por semana. Contar sesionesPorSemana por (asignatura,programa,docente) sin
            // el número de grupo sumaba las 4 secciones como si fueran 4 reuniones de UN grupo —
            // Asignatura.SesionesTeoriaPresencialSemana terminaba en 4, disparando HC-SEP infactible
            // (4 sesiones/semana del mismo tipo no caben con separación mínima de 2 días) para
            // TODOS los grupos de esa asignatura, no solo el que realmente lo ameritaría.
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            string[] cabeceras = { "FACULTAD", "PROGRAMA", "ASIGNATURA", "GRUPO", "DOCENTE", "REALES [h]", "ESPACIO", "DIA", "HORA", "FINAL" };
            for (int c = 0; c < cabeceras.Length; c++) hoja.Cells[1, c + 1].Value = cabeceras[c];

            void Fila(int fila, string grupo, string dia, string horaIni, string horaFin)
            {
                hoja.Cells[fila, 1].Value = "INGENIERIA";
                hoja.Cells[fila, 2].Value = "FACULTAD DE INGENIERIA";
                hoja.Cells[fila, 3].Value = "QUIMICA ORGANICA";
                hoja.Cells[fila, 4].Value = grupo;
                hoja.Cells[fila, 5].Value = "NATALI ALFARO PARADA";
                hoja.Cells[fila, 6].Value = "2";
                hoja.Cells[fila, 7].Value = "QUI";
                hoja.Cells[fila, 8].Value = dia;
                hoja.Cells[fila, 9].Value = horaIni;
                hoja.Cells[fila, 10].Value = horaFin;
            }
            Fila(2, "2", "VIERNES", "12:00", "14:00");
            Fila(3, "4", "VIERNES", "18:00", "20:00");
            Fila(4, "5", "SABADO", "6:00", "8:00");
            Fila(5, "1", "SABADO", "8:00", "10:00");

            var stream = new MemoryStream();
            paquete.SaveAs(stream);
            stream.Position = 0;

            var lector = new LectorExcel(NullLogger<LectorExcel>.Instance);
            var resultado = await lector.LeerCurriculumAsync(stream);

            var asignatura = Assert.Single(resultado.Asignaturas);
            Assert.Equal(1, asignatura.SesionesTeoriaPresencialSemana);
            Assert.Equal(4, resultado.Grupos.Count);
        }

        [Fact]
        public async Task LeerCurriculumAsync_FacultadConTypoDeUnaLetra_AvisaSinFusionarAutomaticamente()
        {
            // Regresión real (prueba.xlsx, fila 58): "INGENIERA" en vez de "INGENIERIA" no lo pesca
            // el match case-insensitive del diccionario de facultades — antes creaba una facultad
            // duplicada en silencio. Debe seguir creando las dos (fusionar automáticamente sería
            // peor si algún día sí son dos facultades distintas), pero avisar del posible typo.
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            string[] cabeceras = { "FACULTAD", "PROGRAMA", "ASIGNATURA", "GRUPO", "DOCENTE", "REALES [h]", "ESPACIO", "DIA", "HORA", "FINAL" };
            for (int c = 0; c < cabeceras.Length; c++) hoja.Cells[1, c + 1].Value = cabeceras[c];

            void Fila(int fila, string facultad, string docente, string dia)
            {
                hoja.Cells[fila, 1].Value = facultad;
                hoja.Cells[fila, 2].Value = "INGENIERIA PESQUERA";
                hoja.Cells[fila, 3].Value = "BIOQUIMICA";
                hoja.Cells[fila, 4].Value = "1";
                hoja.Cells[fila, 5].Value = docente;
                hoja.Cells[fila, 6].Value = "2";
                hoja.Cells[fila, 7].Value = "BIO";
                hoja.Cells[fila, 8].Value = dia;
                hoja.Cells[fila, 9].Value = "6:00";
                hoja.Cells[fila, 10].Value = "8:00";
            }
            Fila(2, "INGENIERIA", "Docente A", "LUNES");
            Fila(3, "INGENIERA", "Docente B", "MARTES");

            var stream = new MemoryStream();
            paquete.SaveAs(stream);
            stream.Position = 0;

            var lector = new LectorExcel(NullLogger<LectorExcel>.Instance);
            var resultado = await lector.LeerCurriculumAsync(stream);

            Assert.Equal(2, resultado.Facultades.Count);
            Assert.Contains(resultado.Advertencias, a => a.Contains("INGENIERA") && a.Contains("INGENIERIA"));
        }

        [Fact]
        public async Task LeerCurriculumAsync_FacultadesRealmenteDistintas_NoAvisaDeTypo()
        {
            using var paquete = new ExcelPackage();
            var hoja = paquete.Workbook.Worksheets.Add("Horario");
            string[] cabeceras = { "FACULTAD", "PROGRAMA", "ASIGNATURA", "GRUPO", "DOCENTE", "REALES [h]", "ESPACIO", "DIA", "HORA", "FINAL" };
            for (int c = 0; c < cabeceras.Length; c++) hoja.Cells[1, c + 1].Value = cabeceras[c];

            void Fila(int fila, string facultad, string docente, string dia)
            {
                hoja.Cells[fila, 1].Value = facultad;
                hoja.Cells[fila, 2].Value = "PROGRAMA X";
                hoja.Cells[fila, 3].Value = "ASIGNATURA X";
                hoja.Cells[fila, 4].Value = "1";
                hoja.Cells[fila, 5].Value = docente;
                hoja.Cells[fila, 6].Value = "2";
                hoja.Cells[fila, 7].Value = "SALON1";
                hoja.Cells[fila, 8].Value = dia;
                hoja.Cells[fila, 9].Value = "6:00";
                hoja.Cells[fila, 10].Value = "8:00";
            }
            Fila(2, "INGENIERIA", "Docente A", "LUNES");
            Fila(3, "INGENIERIA CIVIL", "Docente B", "MARTES");

            var stream = new MemoryStream();
            paquete.SaveAs(stream);
            stream.Position = 0;

            var lector = new LectorExcel(NullLogger<LectorExcel>.Instance);
            var resultado = await lector.LeerCurriculumAsync(stream);

            Assert.Equal(2, resultado.Facultades.Count);
            Assert.Empty(resultado.Advertencias);
        }
    }
}
