using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Excel;
using Xunit;

namespace SOEA.Tests.Infrastructure.Excel
{
    /// <summary>
    /// Revisión previa al import (2026-10-03): casos reales del Excel de Rosa que antes se
    /// importaban en silencio y dejaban el horario infactible o incompleto.
    /// </summary>
    public class ValidarFilasCurriculumTests
    {
        private static readonly LectorExcel Lector = new(NullLogger<LectorExcel>.Instance);

        private static FilaCurriculum Fila(int n, string asig = "FUNDAMENTOS QUIMICA", string grupo = "1",
            string reales = "2", string dia = "MIERCOLES", string hora = "06:00", string final = "08:00",
            string espacio = "QUI", string docente = "ANA", string facultad = "CIENCIAS")
            => new(n, facultad, "LIC QUIMICA", asig, "", "", espacio, reales, dia, hora, final, docente, grupo);

        [Fact]
        public void FilaCoherente_NoTieneIncoherencias()
            => Assert.Empty(Lector.ValidarFilas(new[] { Fila(2) }));

        [Fact]
        public void DuracionDistintaDeLaFranja_EsError()
        {
            var r = Lector.ValidarFilas(new[] { Fila(2, reales: "3") });
            Assert.Contains(r, i => i.Fila == 2 && i.EsError && i.Campo == "duracion" && i.Mensaje.Contains("hay 2 h"));
        }

        [Fact]
        public void MismaAsignaturaConDuracionesDistintas_EsErrorEnTodasSusFilas()
        {
            var r = Lector.ValidarFilas(new[]
            {
                Fila(2, reales: "3", final: "09:00"),
                Fila(3, grupo: "2", dia: "LUNES", hora: "20:00", final: "22:00"),
            });
            Assert.All(new[] { 2, 3 }, n => Assert.Contains(r, i => i.Fila == n && i.EsError && i.Mensaje.Contains("distinta duración")));
        }

        [Fact]
        public void FacultadVacia_EsError_EnVezDeDescartarseEnSilencio()
        {
            var r = Lector.ValidarFilas(new[] { Fila(2, facultad: "") });
            Assert.Contains(r, i => i.EsError && i.Campo == "facultad");
        }

        [Fact]
        public void FilaRepetida_EsError_YChoqueDeAulaODocente_EsAviso()
        {
            var r = Lector.ValidarFilas(new[]
            {
                Fila(2), Fila(3),                                       // misma clase dos veces
                Fila(4, asig: "QUIMICA GENERAL", grupo: "7"),            // otro grupo, mismo aula/docente/hora
            });
            Assert.Contains(r, i => i.Fila == 3 && i.EsError && i.Mensaje.Contains("Fila repetida"));
            Assert.Contains(r, i => i.Fila == 4 && !i.EsError && i.Campo == "espacio");
            Assert.Contains(r, i => i.Fila == 4 && !i.EsError && i.Campo == "docente");
            Assert.DoesNotContain(r, i => i.Fila == 4 && i.EsError);
        }

        [Fact]
        public void DiaIlegibleYFinalAnteriorAlInicio_SonError()
        {
            var r = Lector.ValidarFilas(new[] { Fila(2, dia: "XYZ"), Fila(3, dia: "MARTES", hora: "10:00", final: "08:00") });
            Assert.Contains(r, i => i.Fila == 2 && i.EsError && i.Campo == "dia");
            Assert.Contains(r, i => i.Fila == 3 && i.EsError && i.Campo == "final");
        }
    }
}
