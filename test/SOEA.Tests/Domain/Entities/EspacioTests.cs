using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class EspacioTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly string _validNombre = "Aula 204";
        private readonly TipoEspacio _validTipo = TipoEspacio.Salon;
        private readonly int _validCapacidad = 40;
        private readonly string _validEdificio = "Edificio A";
        private readonly int _validPiso = 2;

        [Fact]
        public void Constructor_WithValidData_CreatesEspacio()
        {
            // Act
            var espacio = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad, _validEdificio, _validPiso);

            // Assert
            Assert.Equal(_validId, espacio.Id);
            Assert.Equal(_validNombre, espacio.Nombre);
            Assert.Equal(_validTipo, espacio.Tipo);
            Assert.Equal(_validCapacidad, espacio.Capacidad);
            Assert.Equal(_validEdificio, espacio.Edificio);
            Assert.Equal(_validPiso, espacio.Piso);
        }

        [Fact]
        public void Constructor_WithoutBuildingAndFloor_CreatesEspacioWithNullValues()
        {
            // Act
            var espacio = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad);

            // Assert
            Assert.Equal(_validId, espacio.Id);
            Assert.Null(espacio.Edificio);
            Assert.Null(espacio.Piso);
        }

        [Fact]
        public void Constructor_WithEmptyId_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Espacio(Guid.Empty, _validNombre, _validTipo, _validCapacidad));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithEmptyNombre_ThrowsArgumentException(string nombre)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Espacio(_validId, nombre, _validTipo, _validCapacidad));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Constructor_WithInvalidCapacidad_ThrowsArgumentException(int capacidad)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Espacio(_validId, _validNombre, _validTipo, capacidad));
        }

        [Fact]
        public void ActualizarDatos_WithValidValues_UpdatesProperties()
        {
            // Arrange
            var espacio = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad, _validEdificio, _validPiso);

            // Act
            espacio.ActualizarDatos("Laboratorio 101", TipoEspacio.Laboratorio, "Edificio B", 1);

            // Assert
            Assert.Equal("Laboratorio 101", espacio.Nombre);
            Assert.Equal(TipoEspacio.Laboratorio, espacio.Tipo);
            Assert.Equal("Edificio B", espacio.Edificio);
            Assert.Equal(1, espacio.Piso);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void ActualizarCapacidad_WithInvalidCapacidad_ThrowsArgumentException(int capacidad)
        {
            // Arrange
            var espacio = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad, _validEdificio, _validPiso);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => espacio.ActualizarCapacidad(capacidad));
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var espacio1 = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad, _validEdificio, _validPiso);
            var espacio2 = new Espacio(_validId, "Aula 305", TipoEspacio.Laboratorio, 30, "Edificio B", 3);

            // Act & Assert
            Assert.Equal(espacio1, espacio2);
        }

        [Fact]
        public void GetHashCode_WithSameId_ReturnsSameHashCode()
        {
            // Arrange
            var espacio1 = new Espacio(_validId, _validNombre, _validTipo, _validCapacidad, _validEdificio, _validPiso);
            var espacio2 = new Espacio(_validId, "Aula 305", TipoEspacio.Laboratorio, 30, "Edificio B", 3);

            // Act & Assert
            Assert.Equal(espacio1.GetHashCode(), espacio2.GetHashCode());
        }

        [Theory]
        [InlineData(TipoEspacio.Salon)]
        [InlineData(TipoEspacio.Laboratorio)]
        [InlineData(TipoEspacio.Auditorio)]
        public void Constructor_WithDifferentTipoEspacio_CreatesCorrectly(TipoEspacio tipo)
        {
            // Act
            var espacio = new Espacio(_validId, _validNombre, tipo, _validCapacidad);

            // Assert
            Assert.Equal(tipo, espacio.Tipo);
        }
    }
}
