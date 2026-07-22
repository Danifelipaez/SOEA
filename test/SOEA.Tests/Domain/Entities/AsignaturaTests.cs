using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class AsignaturaTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly Guid _validProgramaId = Guid.NewGuid();

        // Shape legado (pre-desglose por tipo) — ejercita el constructor de compatibilidad que
        // usan LectorExcel/ImportarCurriculumService. Ver Constructor_ShapeLegado_* más abajo.
        private Asignatura CrearValida(
            CategoriaAsignatura categoria = CategoriaAsignatura.Obligatoria,
            TimeOnly? horaInicioMin = null,
            TimeOnly? horaFinMax = null)
            => new Asignatura(
                _validId,
                "Química General",
                "QUIM-101",
                horasPorSesion: 3,
                sesionesPorSemana: 1,
                sesionesLaboratorioSemestre: 8,
                programaId: _validProgramaId,
                umbralTipoA: Asignatura.UmbralTipoAPorDefecto,
                categoria: categoria,
                horaInicioMin: horaInicioMin,
                horaFinMax: horaFinMax);

        // Shape nuevo (desglose por tipo de sesión): teoría presencial / teoría virtual / laboratorio.
        private Asignatura CrearConTracks(
            int sesionesTeoriaPresencialSemana = 2, int horasTeoriaPresencial = 2,
            int sesionesTeoriaVirtualSemana = 0, int horasTeoriaVirtual = 2,
            int sesionesLaboratorioSemana = 0, int horasLaboratorio = 2,
            int sesionesLaboratorioSemestre = 0,
            CategoriaAsignatura categoria = CategoriaAsignatura.Obligatoria)
            => new Asignatura(
                _validId, "Química General", "QUIM-101",
                sesionesTeoriaPresencialSemana, horasTeoriaPresencial,
                sesionesTeoriaVirtualSemana, horasTeoriaVirtual,
                sesionesLaboratorioSemana, horasLaboratorio,
                sesionesLaboratorioSemestre, _validProgramaId,
                categoria: categoria);

        [Fact]
        public void Constructor_SinCategoriaExplicita_UsaObligatoriaYVentanaNula()
        {
            // Act
            var asignatura = new Asignatura(
                _validId,
                "Química General",
                "QUIM-101",
                3,
                1,
                8,
                _validProgramaId);

            // Assert — defaults conservadores del andamiaje presencial-first
            Assert.Equal(CategoriaAsignatura.Obligatoria, asignatura.Categoria);
            Assert.Null(asignatura.HoraInicioMin);
            Assert.Null(asignatura.HoraFinMax);
        }

        [Fact]
        public void Constructor_ShapeLegado_MapeaATeoriaPresencialYCeraElResto()
        {
            // Act — el constructor legado mapea horasPorSesion/sesionesPorSemana a teoría
            // presencial y deja teoría virtual y laboratorio en 0 (guarda de regresión para
            // LectorExcel/ImportarCurriculumService/ImportController, que siguen usando este shape).
            var asignatura = new Asignatura(
                _validId, "Química General", "QUIM-101",
                horasPorSesion: 3, sesionesPorSemana: 2, sesionesLaboratorioSemestre: 8,
                programaId: _validProgramaId);

            // Assert
            Assert.Equal(2, asignatura.SesionesTeoriaPresencialSemana);
            Assert.Equal(3, asignatura.HorasTeoriaPresencial);
            Assert.Equal(0, asignatura.SesionesTeoriaVirtualSemana);
            Assert.Equal(0, asignatura.SesionesLaboratorioSemana);
            Assert.Equal(8, asignatura.SesionesLaboratorioSemestre);
        }

        [Fact]
        public void Constructor_ConTracks_AsignaLosTresIndependientemente()
        {
            // Act
            var asignatura = CrearConTracks(
                sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2,
                sesionesTeoriaVirtualSemana: 1, horasTeoriaVirtual: 2,
                sesionesLaboratorioSemana: 1, horasLaboratorio: 3,
                sesionesLaboratorioSemestre: 8);

            // Assert
            Assert.Equal(2, asignatura.SesionesTeoriaPresencialSemana);
            Assert.Equal(2, asignatura.HorasTeoriaPresencial);
            Assert.Equal(1, asignatura.SesionesTeoriaVirtualSemana);
            Assert.Equal(2, asignatura.HorasTeoriaVirtual);
            Assert.Equal(1, asignatura.SesionesLaboratorioSemana);
            Assert.Equal(3, asignatura.HorasLaboratorio);
        }

        [Fact]
        public void Constructor_LosTresConteosEnCero_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrearConTracks(
                sesionesTeoriaPresencialSemana: 0,
                sesionesTeoriaVirtualSemana: 0,
                sesionesLaboratorioSemana: 0));
        }

        [Fact]
        public void Constructor_ConteoPositivoYHorasEnCero_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => CrearConTracks(
                sesionesTeoriaPresencialSemana: 0,
                sesionesTeoriaVirtualSemana: 1, horasTeoriaVirtual: 0));
        }

        [Fact]
        public void Constructor_ConCategoriaYVentanaValida_AsignaValores()
        {
            // Act
            var asignatura = CrearValida(
                CategoriaAsignatura.Electiva,
                new TimeOnly(6, 0),
                new TimeOnly(12, 0));

            // Assert
            Assert.Equal(CategoriaAsignatura.Electiva, asignatura.Categoria);
            Assert.Equal(new TimeOnly(6, 0), asignatura.HoraInicioMin);
            Assert.Equal(new TimeOnly(12, 0), asignatura.HoraFinMax);
        }

        [Fact]
        public void Constructor_ConVentanaInvertida_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                CrearValida(
                    CategoriaAsignatura.Obligatoria,
                    new TimeOnly(12, 0),
                    new TimeOnly(6, 0)));
        }

        [Fact]
        public void EstablecerVentanaHoraria_ConRangoValido_Actualiza()
        {
            // Arrange
            var asignatura = CrearValida();

            // Act
            asignatura.EstablecerVentanaHoraria(new TimeOnly(7, 0), new TimeOnly(11, 0));

            // Assert
            Assert.Equal(new TimeOnly(7, 0), asignatura.HoraInicioMin);
            Assert.Equal(new TimeOnly(11, 0), asignatura.HoraFinMax);

            // Act — limpiar acotamiento
            asignatura.EstablecerVentanaHoraria(null, null);

            // Assert
            Assert.Null(asignatura.HoraInicioMin);
            Assert.Null(asignatura.HoraFinMax);
        }

        [Fact]
        public void EstablecerVentanaHoraria_ConRangoInvertido_ThrowsArgumentException()
        {
            var asignatura = CrearValida();
            Assert.Throws<ArgumentException>(() =>
                asignatura.EstablecerVentanaHoraria(new TimeOnly(13, 0), new TimeOnly(9, 0)));
        }

        [Fact]
        public void EstablecerCategoria_Actualiza()
        {
            // Arrange
            var asignatura = CrearValida();

            // Act
            asignatura.EstablecerCategoria(CategoriaAsignatura.Optativa);

            // Assert
            Assert.Equal(CategoriaAsignatura.Optativa, asignatura.Categoria);
        }

        [Fact]
        public void EsCandidataAlternancia_DefaultFalse_YEstablecerElegibilidadActualiza()
        {
            var asignatura = CrearValida();
            Assert.False(asignatura.EsCandidataAlternancia);

            asignatura.EstablecerElegibilidadAlternancia(true);
            Assert.True(asignatura.EsCandidataAlternancia);

            asignatura.EstablecerElegibilidadAlternancia(false);
            Assert.False(asignatura.EsCandidataAlternancia);
        }

        [Fact]
        public void ActualizarDatos_SinCategoria_ConservaLaActual()
        {
            // Arrange — llama al ActualizarDatos legado (6 args posicionales)
            var asignatura = CrearValida(CategoriaAsignatura.Electiva);

            // Act
            asignatura.ActualizarDatos(
                "Química General II",
                "QUIM-102",
                3,
                1,
                8,
                _validProgramaId);

            // Assert — categoría no se pisa al no pasarla
            Assert.Equal(CategoriaAsignatura.Electiva, asignatura.Categoria);
            Assert.Equal("Química General II", asignatura.Nombre);
        }

        [Fact]
        public void ActualizarDatos_ConTracks_ActualizaLosTresIndependientemente()
        {
            // Arrange
            var asignatura = CrearConTracks(sesionesTeoriaPresencialSemana: 2, horasTeoriaPresencial: 2);

            // Act
            asignatura.ActualizarDatos(
                "Química General II", "QUIM-102",
                sesionesTeoriaPresencialSemana: 1, horasTeoriaPresencial: 2,
                sesionesTeoriaVirtualSemana: 1, horasTeoriaVirtual: 2,
                sesionesLaboratorioSemana: 1, horasLaboratorio: 3,
                sesionesLaboratorioSemestre: 8, programaId: _validProgramaId);

            // Assert
            Assert.Equal(1, asignatura.SesionesTeoriaPresencialSemana);
            Assert.Equal(1, asignatura.SesionesTeoriaVirtualSemana);
            Assert.Equal(1, asignatura.SesionesLaboratorioSemana);
            Assert.Equal(3, asignatura.HorasLaboratorio);
        }
    }
}
