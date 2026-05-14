// test/SOEA.Tests/Domain/ValueObjects/TimeSlotTests.cs
using Xunit;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Tests.Domain.ValueObjects
{
    public class IntervaloTiempoTests
    {
        [Fact]
        public void Constructor_IntervaloValido_CreaExitosamente()
        {
            // Arrange
            var startTime = new TimeOnly(8, 0);
            var endTime = new TimeOnly(10, 0);

            // Act
            var intervalo = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, startTime, endTime);

            // Assert
            Assert.Equal(startTime, intervalo.HoraInicio);
            Assert.Equal(endTime, intervalo.HoraFin);
            Assert.Equal(DiaDeSemana.Lunes, intervalo.Dia);
        }

        [Fact]
        public void Constructor_InicioAntesDeHorasOperacion_LanzaExcepcion()
        {
            // Arrange
            var invalidStart = new TimeOnly(5, 59); // Antes de 06:00
            var validEnd = new TimeOnly(8, 0);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(
                () => new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, invalidStart, validEnd));
            Assert.Contains("HC-T01", ex.Message);
        }

        [Fact]
        public void Constructor_FinDespuesDeHorasOperacion_LanzaExcepcion()
        {
            // Arrange
            var validStart = new TimeOnly(20, 0);
            var invalidEnd = new TimeOnly(22, 1); // Después de 22:00

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(
                () => new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Viernes, validStart, invalidEnd));
            Assert.Contains("HC-T01", ex.Message);
        }

        [Fact]
        public void Constructor_FinDespuesDeHorasOperacionSabado_LanzaExcepcion()
        {
            // Arrange
            var validStart = new TimeOnly(13, 0);
            var invalidEnd = new TimeOnly(14, 1); // Después de 14:00 en Sabado

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(
                () => new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Sábado, validStart, invalidEnd));
            Assert.Contains("HC-T01", ex.Message);
        }

        [Fact]
        public void Constructor_FinIgualOAntesDeInicio_LanzaExcepcion()
        {
            // Arrange
            var start = new TimeOnly(10, 0);
            var end = new TimeOnly(10, 0); // Igual

            // Act & Assert
            Assert.Throws<ArgumentException>(
                () => new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Miercoles, start, end));
        }

        [Fact]
        public void GetDuracionEnHoras_RetornaDuracionCorrecta()
        {
            // Arrange
            var timeSlot = new IntervaloTiempo(
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
        public void SuperponeCon_MismoDiaConSuperposicion_RetornaTrue()
        {
            // Arrange
            var slot1 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(9, 0), new TimeOnly(11, 0));

            // Act & Assert
            Assert.True(slot1.SuperponeCon(slot2));
            Assert.True(slot2.SuperponeCon(slot1)); // Simétrico
        }

        [Fact]
        public void SuperponeCon_MismoDiaSinSuperposicion_RetornaFalse()
        {
            // Arrange
            var slot1 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(10, 0), new TimeOnly(12, 0));

            // Act & Assert
            Assert.False(slot1.SuperponeCon(slot2));
        }

        [Fact]
        public void SuperponeCon_DiasDiferentes_RetornaFalse()
        {
            // Arrange
            var slot1 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(10, 0));
            var slot2 = new IntervaloTiempo(Guid.NewGuid(), DiaDeSemana.Martes, new TimeOnly(8, 0), new TimeOnly(10, 0));

            // Act & Assert
            Assert.False(slot1.SuperponeCon(slot2));
        }  
    }
}
