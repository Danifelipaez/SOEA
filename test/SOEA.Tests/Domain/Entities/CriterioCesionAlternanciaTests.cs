using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class CriterioCesionAlternanciaTests
    {
        [Fact]
        public void Constructor_ActivoPorDefecto_True()
        {
            var c = new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Electiva, 1);
            Assert.True(c.Activo);
            Assert.Equal(1, c.Orden);
        }

        [Fact]
        public void Constructor_OrdenNoPositivo_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Elegible, 0));
        }

        [Fact]
        public void Reordenar_ActualizaOrden()
        {
            var c = new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Electiva, 1);
            c.Reordenar(2);
            Assert.Equal(2, c.Orden);
        }

        [Fact]
        public void Reordenar_OrdenNoPositivo_ThrowsArgumentException()
        {
            var c = new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Electiva, 1);
            Assert.Throws<ArgumentException>(() => c.Reordenar(-1));
        }

        [Fact]
        public void EstablecerActivo_Actualiza()
        {
            var c = new CriterioCesionAlternancia(Guid.NewGuid(), CriterioElegibilidadAlternancia.Elegible, 2);
            c.EstablecerActivo(false);
            Assert.False(c.Activo);
        }
    }
}
