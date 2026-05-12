using System;
using System.Collections.Generic;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class HorarioTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly List<Guid> _validSesionIds = new() { Guid.NewGuid(), Guid.NewGuid() };

        [Fact]
        public void Constructor_WithValidData_CreatesHorario()
        {
            // Act
            var horario = new Horario(_validId, "2024-I", _validSesionIds, 0, 85.5m);

            // Assert
            Assert.Equal(_validId, horario.Id);
            Assert.Equal("2024-I", horario.Semestre);
            Assert.Equal(_validSesionIds, horario.SesioneIds);
            Assert.Equal(0, horario.ViolacionesRestriccionesDuras);
            Assert.Equal(85.5m, horario.PuntajeFitness);
            Assert.Equal(EstadoHorario.Borrador, horario.Estado);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidSemestre_ThrowsArgumentException(string semestre)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Horario(_validId, semestre, _validSesionIds));
        }

        [Fact]
        public void Constructor_WithNullSesionIds_ThrowsArgumentException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                new Horario(_validId, "2024-I", null));
            
            Assert.Contains("sesión", ex.Message.ToLower());
        }

        [Fact]
        public void Constructor_WithEmptySesionIds_ThrowsArgumentException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                new Horario(_validId, "2024-I", new List<Guid>()));
            
            Assert.Contains("sesión", ex.Message.ToLower());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-5)]
        public void Constructor_WithNegativeHardConstraintViolations_ThrowsArgumentException(int violations)
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                new Horario(_validId, "2024-I", _validSesionIds, violations));
            
            Assert.Contains("violaciones", ex.Message.ToLower());
        }

        [Fact]
        public void MarcarComoPublicado_WithZeroViolations_UpdatesEstado()
        {
            // Arrange
            var horario = new Horario(_validId, "2024-I", _validSesionIds, 0, 90m);

            // Act
            horario.MarcarComoPublicado();

            // Assert
            Assert.Equal(EstadoHorario.Publicado, horario.Estado);
        }

        [Fact]
        public void MarcarComoPublicado_WithViolations_ThrowsInvalidOperationException()
        {
            // Arrange
            var horario = new Horario(_validId, "2024-I", _validSesionIds, 3, 50m);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                horario.MarcarComoPublicado());
            
            Assert.Contains("violaciones", ex.Message.ToLower());
        }

        [Fact]
        public void ActualizarFitnessScore_WithValidScore_UpdatesScore()
        {
            // Arrange
            var horario = new Horario(_validId, "2024-I", _validSesionIds, 0, 50m);

            // Act
            horario.ActualizarFitnessScore(95.75m);

            // Assert
            Assert.Equal(95.75m, horario.PuntajeFitness);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-0.1)]
        public void ActualizarFitnessScore_WithNegativeScore_ThrowsArgumentException(decimal score)
        {
            // Arrange
            var horario = new Horario(_validId, "2024-I", _validSesionIds);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                horario.ActualizarFitnessScore(score));
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var horario1 = new Horario(_validId, "2024-I", _validSesionIds);
            var horario2 = new Horario(_validId, "2024-II", _validSesionIds);

            // Act & Assert
            Assert.Equal(horario1, horario2);
        }

        [Fact]
        public void Equals_WithDifferentId_ReturnsFalse()
        {
            // Arrange
            var horario1 = new Horario(_validId, "2024-I", _validSesionIds);
            var horario2 = new Horario(Guid.NewGuid(), "2024-I", _validSesionIds);

            // Act & Assert
            Assert.NotEqual(horario1, horario2);
        }

        [Fact]
        public void GetHashCode_WithSameId_ReturnsSameHashCode()
        {
            // Arrange
            var horario1 = new Horario(_validId, "2024-I", _validSesionIds);
            var horario2 = new Horario(_validId, "2024-II", new() { Guid.NewGuid() });

            // Act & Assert
            Assert.Equal(horario1.GetHashCode(), horario2.GetHashCode());
        }

        [Fact]
        public void Constructor_SetsGeneratedAtToCurrentTime()
        {
            // Arrange
            var beforeCreation = DateTime.UtcNow;

            // Act
            var horario = new Horario(_validId, "2024-I", _validSesionIds);
            var afterCreation = DateTime.UtcNow;

            // Assert
            Assert.True(beforeCreation <= horario.GeneradoEn && horario.GeneradoEn <= afterCreation);
        }

        [Fact]
        public void Constructor_InitializesEstadoAsBorrador()
        {
            // Act
            var horario = new Horario(_validId, "2024-I", _validSesionIds);

            // Assert
            Assert.Equal(EstadoHorario.Borrador, horario.Estado);
        }

        [Fact]
        public void Constructor_WithDefaultConstraintValues_InitializesCorrectly()
        {
            // Act
            var horario = new Horario(_validId, "2024-I", _validSesionIds);

            // Assert
            Assert.Equal(0, horario.ViolacionesRestriccionesDuras);
            Assert.Equal(0m, horario.PuntajeFitness);
        }
    }
}
