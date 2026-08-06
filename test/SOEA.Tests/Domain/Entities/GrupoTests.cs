using System;
using System.Collections.Generic;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Tests.Domain.Entities
{
    public class GrupoTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly Guid _validProgramaId = Guid.NewGuid();

        [Fact]
        public void Constructor_WithValidData_CreatesGrupo()
        {
            // Act
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30, TipoAlternancia.TipoA);

            // Assert
            Assert.Equal(_validId, grupo.Id);
            Assert.Equal("Grupo A", grupo.Nombre);
            Assert.Equal(_validProgramaId, grupo.ProgramaId);
            Assert.Equal(30, grupo.EstudiantesInscritos);
            Assert.Equal(TipoAlternancia.TipoA, grupo.Alternancia);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithInvalidNombre_ThrowsArgumentException(string nombre)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new Grupo(_validId, nombre, _validProgramaId, 30));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Constructor_WithInvalidEstudiantes_ThrowsArgumentException(int estudiantes)
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                new Grupo(_validId, "Grupo A", _validProgramaId, estudiantes));

            Assert.Contains("estudiantes", ex.Message.ToLower());
        }

        [Fact]
        public void Constructor_WithDefaultAlternancia_SetsSinAlternancia()
        {
            // Act
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            // Assert
            Assert.Equal(TipoAlternancia.SinAlternancia, grupo.Alternancia);
        }

        [Fact]
        public void Constructor_WithDocente_SetsDocenteId()
        {
            // Arrange
            var docenteId = Guid.NewGuid();

            // Act
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30, docenteId: docenteId);

            // Assert
            Assert.Equal(docenteId, grupo.DocenteId);
        }

        [Fact]
        public void AsignarDocente_UpdatesDocenteId()
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            var docenteId = Guid.NewGuid();

            // Act
            grupo.AsignarDocente(docenteId);

            // Assert
            Assert.Equal(docenteId, grupo.DocenteId);

            // Act: desasignar
            grupo.AsignarDocente(null);
            Assert.Null(grupo.DocenteId);
        }

        [Fact]
        public void ActualizarNombre_WithValidNombre_Updates()
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            // Act
            grupo.ActualizarNombre("Grupo B");

            // Assert
            Assert.Equal("Grupo B", grupo.Nombre);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ActualizarNombre_WithInvalidNombre_ThrowsArgumentException(string nombre)
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => grupo.ActualizarNombre(nombre));
        }

        [Fact]
        public void ActualizarEstudiantes_WithValidCantidad_Updates()
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            // Act
            grupo.ActualizarEstudiantes(45);

            // Assert
            Assert.Equal(45, grupo.EstudiantesInscritos);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void ActualizarEstudiantes_WithInvalidCantidad_ThrowsArgumentException(int cantidad)
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => grupo.ActualizarEstudiantes(cantidad));
        }

        [Fact]
        public void ActualizarAlternancia_WithValidAlternancia_Updates()
        {
            // Arrange
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30, TipoAlternancia.TipoA);

            // Act
            grupo.ActualizarAlternancia(TipoAlternancia.TipoB);

            // Assert
            Assert.Equal(TipoAlternancia.TipoB, grupo.Alternancia);
        }

        [Fact]
        public void ObtenerDisponibilidadSemanal_SinJson_EsSinRestriccion()
        {
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);

            var disp = grupo.ObtenerDisponibilidadSemanal();

            Assert.True(disp.PermiteBloque(DiaDeSemana.Sábado, new TimeOnly(20, 0), new TimeOnly(21, 0)));
        }

        [Fact]
        public void ActualizarDisponibilidadUi_DerivaLaMismaRestriccionEnObtenerDisponibilidadSemanal()
        {
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            grupo.ActualizarDisponibilidadUi("""{"lunes":{"noDisponible":true}}""");

            var disp = grupo.ObtenerDisponibilidadSemanal();

            Assert.False(disp.PermiteBloque(DiaDeSemana.Lunes, new TimeOnly(8, 0), new TimeOnly(9, 0)));
            Assert.True(disp.PermiteBloque(DiaDeSemana.Martes, new TimeOnly(8, 0), new TimeOnly(9, 0)));
        }

        // Regresión P1.4: GruposController persiste RequisitosEspacio a través de este mutador —
        // reemplaza a Asignatura.EspacioFijoId (un solo uuid por asignatura, sólo laboratorio).
        [Fact]
        public void ActualizarRequisitosEspacio_ReemplazaLaListaCompleta()
        {
            var grupo = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            var espacioId = Guid.NewGuid();

            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.Laboratorio, espacioId, TipoEspacio.Laboratorio, Sesiones: 1)
            });

            var requisito = Assert.Single(grupo.RequisitosEspacio);
            Assert.Equal(TipoSesion.Laboratorio, requisito.TipoSesion);
            Assert.Equal(espacioId, requisito.EspacioId);

            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>());
            Assert.Empty(grupo.RequisitosEspacio);
        }

        [Fact]
        public void Equals_WithSameId_ReturnsTrue()
        {
            // Arrange
            var grupo1 = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            var grupo2 = new Grupo(_validId, "Grupo B", Guid.NewGuid(), 50);

            // Act & Assert
            Assert.Equal(grupo1, grupo2);
        }

        [Fact]
        public void Equals_WithDifferentId_ReturnsFalse()
        {
            // Arrange
            var grupo1 = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            var grupo2 = new Grupo(Guid.NewGuid(), "Grupo A", _validProgramaId, 30);

            // Act & Assert
            Assert.NotEqual(grupo1, grupo2);
        }

        [Fact]
        public void GetHashCode_WithSameId_ReturnsSameHashCode()
        {
            // Arrange
            var grupo1 = new Grupo(_validId, "Grupo A", _validProgramaId, 30);
            var grupo2 = new Grupo(_validId, "Grupo B", Guid.NewGuid(), 50);

            // Act & Assert
            Assert.Equal(grupo1.GetHashCode(), grupo2.GetHashCode());
        }
    }
}
