using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Services;
using Xunit;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// Verifica la detección de docentes fragmentados por variantes de nombre (causa raíz del
    /// síntoma "un docente con 2 sesiones a la misma hora"). El detector reporta, no fusiona.
    /// </summary>
    public class DetectorDocentesDuplicadosTests
    {
        private static DetectorDocentesDuplicados.Docente D(string nombre) =>
            new(Guid.NewGuid(), nombre);

        [Fact]
        public void Truncamiento_DeApellido_SeDetecta()
        {
            // Caso real del dashboard: "Macias Vil" vs "Macias Villamizar".
            var docentes = new[]
            {
                D("Victor Enrique Macias Vil"),
                D("Victor Macias Villamizar")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Single(grupos);
            Assert.Equal(2, grupos[0].Count);
        }

        [Fact]
        public void Variante_SoloMayusculasYTildes_SeDetecta()
        {
            // Subconjunto idéntico tras normalizar.
            var docentes = new[]
            {
                D("Johanna Marcela Florez Ca"),
                D("JOHANNA MARCELA FLOREZ CA")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Single(grupos);
        }

        [Fact]
        public void SegundoNombreFaltante_EsSubconjunto_SeDetecta()
        {
            var docentes = new[]
            {
                D("Jose Luis Ropero Vega"),
                D("Jose Ropero Vega")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Single(grupos);
        }

        [Fact]
        public void PersonasDistintas_MismoApellido_NoSeDetectan()
        {
            // Mismo apellido, distinto nombre de pila → NO son duplicados.
            var docentes = new[]
            {
                D("Maria Garcia Lopez"),
                D("Pedro Garcia Lopez")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Empty(grupos);
        }

        [Fact]
        public void MismoNombre_ApellidosDistintos_NoSeDetectan()
        {
            // Mismo nombre de pila pero apellidos sin relación → NO duplicados.
            var docentes = new[]
            {
                D("Carlos Andres Molina Ramirez"),
                D("Carlos Perez Soto")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Empty(grupos);
        }

        [Fact]
        public void ListaSinDuplicados_DevuelveVacio()
        {
            var docentes = new[]
            {
                D("Ana Torres"),
                D("Luis Borja Hidalgo"),
                D("Marta Hernandez Castro")
            };

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(docentes);

            Assert.Empty(grupos);
        }
    }
}
