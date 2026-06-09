using System;
using System.Collections.Generic;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class DocenteTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly string _validNombre = "Juan";
        private readonly string _validApellido = "Pérez";
        private readonly string _validCorreo = "juan.perez@example.com";
        private readonly decimal _validMaxHoras = 40m;
        private readonly List<FranjaHoraria> _validDisponibilidad = new() { FranjaHoraria.Matutino, FranjaHoraria.Vespertino };

        [Fact]
        public void Constructor_WithValidData_CreatesDocente()
        {
            // Act
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Assert
            Assert.Equal(_validId, docente.Id);
            Assert.Equal(_validNombre, docente.Nombre);
            Assert.Equal(_validApellido, docente.Apellido);
            Assert.Equal(_validCorreo, docente.Correo);
            Assert.Equal(_validMaxHoras, docente.MaximoHorasSemanales);
            Assert.Equal(2, docente.Disponibilidad.Count);
        }

        [Fact]
        public void Constructor_WithEmptyId_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Docente(Guid.Empty, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad));
        }

        public static IEnumerable<object?[]> InvalidNombreCases => new[]
        {
            new object?[] { null },
            new object?[] { "" },
            new object?[] { "   " }
        };

        [Theory]
        [MemberData(nameof(InvalidNombreCases))]
        public void Constructor_WithEmptyNombre_ThrowsArgumentException(string? nombre)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Docente(_validId, nombre!, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad));
        }

        public static IEnumerable<object?[]> InvalidApellidoCases => new[]
        {
            new object?[] { null },
            new object?[] { "" },
            new object?[] { "   " }
        };

        // Apellido es opcional en el piloto (puede venir vacío cuando el nombre completo llega en un solo campo).
        // Se mantiene el test documentando el nuevo comportamiento esperado.
        [Theory]
        [MemberData(nameof(InvalidApellidoCases))]
        public void Constructor_WithEmptyApellido_DoesNotThrow(string? apellido)
        {
            // Act — no debe lanzar excepción (apellido es opcional en el piloto)
            var docente = new Docente(_validId, _validNombre, apellido ?? "", _validCorreo, _validMaxHoras, _validDisponibilidad);
            Assert.NotNull(docente);
        }

        // null, vacío o solo espacios ya no lanzan excepción (correo opcional en el piloto).
        // Solo correos con formato claramente inválido pero no vacíos siguen lanzando.
        public static IEnumerable<object?[]> InvalidCorreoCases => new[]
        {
            new object?[] { "invalid-email" },
            new object?[] { "@example.com" },
            new object?[] { "juan@" }
        };

        [Theory]
        [MemberData(nameof(InvalidCorreoCases))]
        public void Constructor_WithInvalidCorreo_ThrowsArgumentException(string? correo)
        {
            // Act & Assert — correo con formato inválido sigue lanzando
            Assert.Throws<ArgumentException>(() =>
                new Docente(_validId, _validNombre, _validApellido, correo!, _validMaxHoras, _validDisponibilidad));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        [InlineData(-0.5)]
        public void Constructor_WithInvalidMaxHoras_ThrowsArgumentException(decimal maxHoras)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Docente(_validId, _validNombre, _validApellido, _validCorreo, maxHoras, _validDisponibilidad));
        }

        [Fact]
        public void Constructor_WithEmptyDisponibilidad_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, new()));
        }

        [Fact]
        public void NombreCompleto_ReturnsFormattedFullName()
        {
            // Act
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Assert
            Assert.Equal("Juan Pérez", docente.NombreCompleto);
        }

        [Fact]
        public void NombreCompleto_WhenApellidoEmpty_ReturnsNombreOnly()
        {
            var docente = new Docente(_validId, _validNombre, "", _validCorreo, _validMaxHoras, _validDisponibilidad);
            Assert.Equal(_validNombre, docente.NombreCompleto);
        }

        [Fact]
        public void ActualizarDisponibilidad_WithValidDisponibilidad_UpdatesProperty()
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);
            var nuevaDisponibilidad = new List<FranjaHoraria> { FranjaHoraria.Matutino };

            // Act
            docente.ActualizarDisponibilidad(nuevaDisponibilidad);

            // Assert
            Assert.Single(docente.Disponibilidad);
            Assert.Contains(FranjaHoraria.Matutino, docente.Disponibilidad);
        }

        [Fact]
        public void ActualizarDisponibilidad_WithEmptyList_ThrowsArgumentException()
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => docente.ActualizarDisponibilidad(new()));
        }

        [Fact]
        public void ActualizarMaximoHoras_WithValidValue_UpdatesProperty()
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);
            var newMaxHoras = 50m;

            // Act
            docente.ActualizarMaximoHoras(newMaxHoras);

            // Assert
            Assert.Equal(newMaxHoras, docente.MaximoHorasSemanales);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void ActualizarMaximoHoras_WithInvalidValue_ThrowsArgumentException(decimal maxHoras)
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => docente.ActualizarMaximoHoras(maxHoras));
        }

        [Fact]
        public void ActualizarDatos_WithValidValues_UpdatesProperties()
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Act
            docente.ActualizarDatos("Ana", "Gómez", "ana.gomez@example.com", 30m);

            // Assert
            Assert.Equal("Ana", docente.Nombre);
            Assert.Equal("Gómez", docente.Apellido);
            Assert.Equal("ana.gomez@example.com", docente.Correo);
            Assert.Equal(30m, docente.MaximoHorasSemanales);
            Assert.Equal("Ana Gómez", docente.NombreCompleto);
        }

        public static IEnumerable<object?[]> InvalidActualizarDatosCases => new[]
        {
            new object?[] { null, "Gómez", "ana.gomez@example.com", 30m },
            // apellido null ya es aceptado — se omite ese caso
            new object?[] { "Ana", "Gómez", "invalid-email", 30m },
            new object?[] { "Ana", "Gómez", "ana.gomez@example.com", 0m }
        };

        [Theory]
        [MemberData(nameof(InvalidActualizarDatosCases))]
        public void ActualizarDatos_WithInvalidValues_ThrowsArgumentException(string? nombre, string? apellido, string? correo, decimal maxHoras)
        {
            // Arrange
            var docente = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => docente.ActualizarDatos(nombre, apellido, correo, maxHoras));
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var docente1 = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);
            var docente2 = new Docente(_validId, "Pedro", "González", "pedro@example.com", 30m, new() { FranjaHoraria.Vespertino });

            // Act & Assert
            Assert.Equal(docente1, docente2);
        }

        [Fact]
        public void GetHashCode_WithSameId_ReturnsSameHashCode()
        {
            // Arrange
            var docente1 = new Docente(_validId, _validNombre, _validApellido, _validCorreo, _validMaxHoras, _validDisponibilidad);
            var docente2 = new Docente(_validId, "Pedro", "González", "pedro@example.com", 30m, new() { FranjaHoraria.Vespertino });

            // Act & Assert
            Assert.Equal(docente1.GetHashCode(), docente2.GetHashCode());
        }
    }
}
