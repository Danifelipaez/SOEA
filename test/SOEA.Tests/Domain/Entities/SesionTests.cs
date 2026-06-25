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
        public void Constructor_SinDocente_CreaSesion()
        {
            // CR-02 (presencial-first): el docente es opcional. Una sesión sin docente es válida.
            var sesion = new Sesion(
                _validId,
                _validAsignaturaId,
                null,
                _validBloqueId,
                null,
                null,
                TipoAlternancia.TipoA,
                Modalidad.Presencial,
                2m,
                false,
                false);

            Assert.Null(sesion.DocenteId);
            Assert.Equal(EstadoSesion.Pendiente, sesion.Estado);
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
        public void Constructor_SinFlujoExplicito_UsaLaboratorioYPresencialPuro()
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

            // Assert — defaults del andamiaje presencial-first
            Assert.Equal(TipoFlujo.Laboratorio, sesion.TipoFlujo);
            Assert.Null(sesion.PatronAlternanciaId);
            Assert.False(sesion.Bloqueada);
        }

        [Fact]
        public void Constructor_ConFlujoYPatronYBloqueada_AsignaValores()
        {
            // Arrange
            var patronId = Guid.NewGuid();

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
                false,
                TipoFlujo.AulaVirtual,
                patronId,
                true);

            // Assert
            Assert.Equal(TipoFlujo.AulaVirtual, sesion.TipoFlujo);
            Assert.Equal(patronId, sesion.PatronAlternanciaId);
            Assert.True(sesion.Bloqueada);
        }

        [Fact]
        public void Mutadores_ActualizanFlujoPatronYBloqueo()
        {
            // Arrange
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
            var patronId = Guid.NewGuid();

            // Act
            sesion.EstablecerFlujo(TipoFlujo.AulaVirtual);
            sesion.EstablecerPatronAlternancia(patronId);
            sesion.Bloquear();

            // Assert
            Assert.Equal(TipoFlujo.AulaVirtual, sesion.TipoFlujo);
            Assert.Equal(patronId, sesion.PatronAlternanciaId);
            Assert.True(sesion.Bloqueada);

            // Act — revertir
            sesion.EstablecerPatronAlternancia(null);
            sesion.Desbloquear();

            // Assert
            Assert.Null(sesion.PatronAlternanciaId);
            Assert.False(sesion.Bloqueada);
        }

        // ── Mutador AsignarDocente (Etapa 4 / HU-04) ────────────────────────────────

        [Fact]
        public void AsignarDocente_ConGuidValido_AsignaDocente()
        {
            var sesion = new Sesion(
                _validId, _validAsignaturaId, null, _validBloqueId,
                null, null, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);
            var nuevoDocente = Guid.NewGuid();

            sesion.AsignarDocente(nuevoDocente);

            Assert.Equal(nuevoDocente, sesion.DocenteId);
        }

        [Fact]
        public void AsignarDocente_ConNull_DesasignaDocente()
        {
            var sesion = new Sesion(
                _validId, _validAsignaturaId, _validDocenteId, _validBloqueId,
                null, null, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);

            sesion.AsignarDocente(null);

            Assert.Null(sesion.DocenteId);
        }

        [Fact]
        public void AsignarDocente_ConGuidVacio_Lanza()
        {
            var sesion = new Sesion(
                _validId, _validAsignaturaId, null, _validBloqueId,
                null, null, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);

            Assert.Throws<ArgumentException>(() => sesion.AsignarDocente(Guid.Empty));
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
