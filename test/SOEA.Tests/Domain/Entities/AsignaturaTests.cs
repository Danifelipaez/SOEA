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
        public void ActualizarDatos_SinCategoria_ConservaLaActual()
        {
            // Arrange
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
    }
}
