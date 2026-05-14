using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class BloqueTiempoTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly DiaDeSemana _validDia = DiaDeSemana.Lunes;
        private readonly TimeOnly _validHoraInicio = new(9, 0);
        private readonly TimeOnly _validHoraFin = new(11, 0);

        [Fact]
        public void Constructor_WithValidData_CreatesBloqueTiempo()
        {
            // Act
            var bloque = new BloqueTiempo(_validId, _validDia, _validHoraInicio, _validHoraFin);

            // Assert
            Assert.Equal(_validId, bloque.Id);
            Assert.Equal(_validDia, bloque.Dia);
            Assert.Equal(_validHoraInicio, bloque.HoraInicio);
            Assert.Equal(_validHoraFin, bloque.HoraFin);
        }

        [Fact]
        public void Constructor_WithEmptyId_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(Guid.Empty, _validDia, _validHoraInicio, _validHoraFin));
        }

        [Fact]
        public void Constructor_WithStartTimeBeforeAllowedRange_ThrowsArgumentException()
        {
            // Arrange
            var beforeMinTime = new TimeOnly(5, 59);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(_validId, _validDia, beforeMinTime, _validHoraFin));
        }

        [Fact]
        public void Constructor_WithEndTimeAfterAllowedRange_ThrowsArgumentException()
        {
            // Arrange
            var afterMaxTime = new TimeOnly(22, 1);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(_validId, _validDia, _validHoraInicio, afterMaxTime));
        }

        [Fact]
        public void Constructor_WithStartTimeEqualOrGreaterThanEndTime_ThrowsArgumentException()
        {
            // Arrange
            var sameTime = new TimeOnly(11, 0);
            var laterTime = new TimeOnly(12, 0);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(_validId, _validDia, sameTime, sameTime));

            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(_validId, _validDia, laterTime, _validHoraInicio));
        }

        [Fact]
        public void Duracion_CalculatedCorrectly()
        {
            // Arrange
            var bloque = new BloqueTiempo(_validId, _validDia, _validHoraInicio, _validHoraFin);

            // Act
            var duracion = bloque.Duracion;

            // Assert
            Assert.Equal(2m, duracion);
        }

        [Fact]
        public void Duracion_WithDecimalHours_CalculatedCorrectly()
        {
            // Arrange
            var horaInicio = new TimeOnly(9, 0);
            var horaFin = new TimeOnly(10, 30);
            var bloque = new BloqueTiempo(_validId, _validDia, horaInicio, horaFin);

            // Act
            var duracion = bloque.Duracion;

            // Assert
            Assert.Equal(1.5m, duracion);
        }

        [Fact]
        public void Constructor_WithAllValidDaysOfWeek_CreatesSuccessfully()
        {
            // Arrange & Act & Assert
            foreach (DiaDeSemana dia in Enum.GetValues(typeof(DiaDeSemana)))
            {
                var bloque = new BloqueTiempo(Guid.NewGuid(), dia, _validHoraInicio, _validHoraFin);
                Assert.Equal(dia, bloque.Dia);
            }
        }

        [Fact]
        public void Constructor_WithMinimumAllowedRange_CreatesSuccessfully()
        {
            // Arrange
            var minHora = new TimeOnly(6, 0);
            var maxHora = new TimeOnly(6, 30);

            // Act
            var bloque = new BloqueTiempo(_validId, _validDia, minHora, maxHora);

            // Assert
            Assert.Equal(minHora, bloque.HoraInicio);
            Assert.Equal(maxHora, bloque.HoraFin);
        }

        [Fact]
        public void Constructor_WithMaximumAllowedRange_CreatesSuccessfully()
        {
            // Arrange
            var minHora = new TimeOnly(21, 30);
            var maxHora = new TimeOnly(22, 0);

            // Act
            var bloque = new BloqueTiempo(_validId, _validDia, minHora, maxHora);

            // Assert
            Assert.Equal(minHora, bloque.HoraInicio);
            Assert.Equal(maxHora, bloque.HoraFin);
        }

        [Fact]
        public void Constructor_WithEndTimeAfterAllowedRangeSaturday_ThrowsArgumentException()
        {
            // Arrange
            var afterMaxTime = new TimeOnly(14, 1);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new BloqueTiempo(_validId, DiaDeSemana.Sábado, new TimeOnly(13, 0), afterMaxTime));
        }

        [Fact]
        public void Constructor_WithMaximumAllowedRangeSaturday_CreatesSuccessfully()
        {
            // Arrange
            var minHora = new TimeOnly(13, 30);
            var maxHora = new TimeOnly(14, 0);

            // Act
            var bloque = new BloqueTiempo(_validId, DiaDeSemana.Sábado, minHora, maxHora);

            // Assert
            Assert.Equal(minHora, bloque.HoraInicio);
            Assert.Equal(maxHora, bloque.HoraFin);
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var bloque1 = new BloqueTiempo(_validId, DiaDeSemana.Lunes, _validHoraInicio, _validHoraFin);
            var bloque2 = new BloqueTiempo(_validId, DiaDeSemana.Viernes, new TimeOnly(14, 0), new TimeOnly(16, 0));

            // Act & Assert
            Assert.Equal(bloque1, bloque2);
        }

        [Fact]
        public void GetHashCode_WithSameId_ReturnsSameHashCode()
        {
            // Arrange
            var bloque1 = new BloqueTiempo(_validId, DiaDeSemana.Lunes, _validHoraInicio, _validHoraFin);
            var bloque2 = new BloqueTiempo(_validId, DiaDeSemana.Viernes, new TimeOnly(14, 0), new TimeOnly(16, 0));

            // Act & Assert
            Assert.Equal(bloque1.GetHashCode(), bloque2.GetHashCode());
        }
    }
}
