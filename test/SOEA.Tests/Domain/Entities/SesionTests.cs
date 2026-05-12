using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class SesionTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly Guid _validAsignaturaId = Guid.NewGuid();
        private readonly Guid _validDocenteId = Guid.NewGuid();
        private readonly Guid _validBloqueId = Guid.NewGuid();

        [Fact]
        public void Constructor_WithValidData_CreatesSesion()
        {
            // Act
            var sesion = new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false);

            // Assert
            Assert.Equal(_validId, sesion.Id);
            Assert.Equal(_validAsignaturaId, sesion.AsignaturaId);
            Assert.Equal(_validDocenteId, sesion.DocenteId);
            Assert.Equal(_validBloqueId, sesion.BloqueTiempoId);
            Assert.Equal(EstadoSesion.Pendiente, sesion.Estado);
        }

        [Fact]
        public void Constructor_WithEmptyId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                Guid.Empty,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false));
        }

        [Fact]
        public void Constructor_WithEmptyAsignaturaId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                _validId,
                Guid.Empty,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false));
        }

        [Fact]
        public void Constructor_WithEmptyDocenteId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                _validId,
                _validAsignaturaId,
                Guid.Empty,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false));
        }

        [Fact]
        public void Constructor_WithEmptyBloqueId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                Guid.Empty,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(8.1)]
        [InlineData(10)]
        public void Constructor_WithInvalidDuracionHoras_ThrowsArgumentException(decimal horas)
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                horas,
                false,
                false));
        }

        [Fact]
        public void Constructor_WithEsBloqueTrueAndEstaDivididaTrue_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                true,
                true));
        }

        [Fact]
        public void Constructor_WithOptionalProperties_AllowsNullEspacioAndGrupo()
        {
            // Act
            var sesion = new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Virtual,
                1.5m,
                false,
                false);

            // Assert
            Assert.Null(sesion.EspacioId);
            Assert.Null(sesion.GrupoId);
        }

        [Fact]
        public void Constructor_WithBloqueTrue_AllowsEstadoSesionPendiente()
        {
            // Act
            var sesion = new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                Guid.NewGuid(),
                null,
                TipoAlternancia.SinAlternancia,
                Modalidad.Presencial,
                3m,
                true,
                false);

            // Assert
            Assert.Equal(EstadoSesion.Pendiente, sesion.Estado);
            Assert.True(sesion.EsBloque);
            Assert.False(sesion.EstaDividida);
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var sesion1 = new Sesion(
                _validId,
                _validAsignaturaId,
                _validDocenteId,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false);

            var sesion2 = new Sesion(
                _validId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                TipoAlternancia.TipoB,
                Modalidad.Virtual,
                1m,
                true,
                false);

            // Act & Assert
            Assert.Equal(sesion1, sesion2);
        }
    }
}
