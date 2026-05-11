// test/SOEA.Tests/Domain/ValueObjects/TimeSlotTests.cs
using Xunit;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Tests.Domain.ValueObjects
{
    public class TimeSlotTests
    {
        [Fact]
        public void Constructor_ValidTimeSlot_CreatesSuccessfully()
        {
            // Arrange
            var startTime = new TimeOnly(8, 0);
            var endTime = new TimeOnly(10, 0);

            // Act
            var TimeSlop = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, startTime, endTime);

            // Assert
            Assert.Equal(startTime, TimeSlop.HoraInicio);
            Assert.Equal(endTime, TimeSlop.HoraFin);
            Assert.Equal(DiaDeSemana.Lunes, TimeSlop.Dia);
        }

        [Fact]
        public void Constructor_StartTimeBeforeOperatingHours_ThrowsArgumentException()
        {
            // Arrange
            var invalidStart = new TimeOnly(6, 59); // Antes de 07:00
            var validEnd = new TimeOnly(8, 0);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(
                () => new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, invalidStart, validEnd));
            Assert.Contains("HC-T01", ex.Message);
        }

        [Fact]
        public void Constructor_EndTimeAfterOperatingHours_ThrowsArgumentException()
        {
            // Arrange
            var validStart = new TimeOnly(20, 0);
            var invalidEnd = new TimeOnly(21, 31); // Después de 21:30

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(
                () => new TimeSlop(Guid.NewGuid(), DiaDeSemana.Viernes, validStart, invalidEnd));
            Assert.Contains("HC-T01", ex.Message);
        }

        [Fact]
        public void Constructor_EndTimeEqualsOrBeforeStartTime_ThrowsArgumentException()
        {
            // Arrange
            var start = new TimeOnly(10, 0);
            var end = new TimeOnly(10, 0); // Igual

            // Act & Assert
            Assert.Throws<ArgumentException>(
                () => new TimeSlop(Guid.NewGuid(), DiaDeSemana.Miercoles, start, end));
        }

        [Fact]
        public void GetDurationHours_ReturnsCorrectDuration()
        {
            // Arrange
            var timeSlot = new TimeSlop(
                Guid.NewGuid(),
                DiaDeSemana.Martes,
                new TimeOnly(8, 0),
                new TimeOnly(11, 0));

            // Act
            var duration = timeSlot.GetDuracionEnHoras();

            // Assert
            Assert.Equal(3m, duration);
        }

        [Fact]
        public void OverlapsWith_SameDayWithOverlap_ReturnsTrue()
        {
            // Arrange
            var slot1 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(9, 0), new TimeOnly(11, 0));

            // Act & Assert
            Assert.True(slot1.SuperponeCon(slot2));
            Assert.True(slot2.SuperponeCon(slot1)); // Simétrico
        }

        [Fact]
        public void OverlapsWith_SameDayNoOverlap_ReturnsFalse()
        {
            // Arrange
            var slot1 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(10, 0), new TimeOnly(12, 0));

            // Act & Assert
            Assert.False(slot1.SuperponeCon(slot2));
        }

        [Fact]
        public void OverlapsWith_DifferentDays_ReturnsFalse()
        {
            // Arrange
            var slot1 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Martes, new TimeOnly(8, 0), new TimeOnly(10, 0));

            // Act & Assert
            Assert.False(slot1.SuperponeCon(slot2));
        }

        [Fact]
        public void IsValidForLabSession_LabStartingBefore19_30_ReturnsTrue()
        {
            // Arrange
            var timeSlot = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Viernes, new TimeOnly(18, 0), new TimeOnly(21, 30));

            // Act & Assert
            Assert.True(timeSlot.EsValidaParaLaboratorio());
        }

        [Fact]
        public void IsValidForLabSession_LabStartingAt19_30OrLater_ReturnsFalse()
        {
            // Arrange
            var timeSlot = new TimeSlop(Guid.NewGuid(), DiaDeSemana.Viernes, new TimeOnly(19, 31), new TimeOnly(21, 30));

            // Act & Assert
            Assert.False(timeSlot.EsValidaParaLaboratorio());
        }
    }
}