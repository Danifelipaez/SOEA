using System;
using Xunit;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Tests.Domain.Entities
{
    public class AsignacionSemanalTests
    {
        private readonly Guid _validId = Guid.NewGuid();
        private readonly Guid _validSesionId = Guid.NewGuid();
        private readonly Guid _validBloqueId = Guid.NewGuid();
        private readonly Guid _validEspacioId = Guid.NewGuid();

        [Fact]
        public void Constructor_PresencialConEspacio_CreaAsignacion()
        {
            var asignacion = new AsignacionSemanal(
                _validId, _validSesionId, SemanaAcademica.A,
                _validBloqueId, _validEspacioId, Modalidad.Presencial);

            Assert.Equal(_validId, asignacion.Id);
            Assert.Equal(_validSesionId, asignacion.SesionId);
            Assert.Equal(SemanaAcademica.A, asignacion.Semana);
            Assert.Equal(_validBloqueId, asignacion.BloqueTiempoId);
            Assert.Equal(_validEspacioId, asignacion.EspacioId);
            Assert.Equal(Modalidad.Presencial, asignacion.Modalidad);
        }

        [Fact]
        public void Constructor_VirtualSinEspacio_CreaAsignacion()
        {
            var asignacion = new AsignacionSemanal(
                _validId, _validSesionId, SemanaAcademica.B,
                _validBloqueId, null, Modalidad.Virtual);

            Assert.Null(asignacion.EspacioId);
            Assert.Equal(Modalidad.Virtual, asignacion.Modalidad);
        }

        // Invariante regla 9: una asignación virtual nunca puede ocupar espacio físico.
        [Fact]
        public void Constructor_VirtualConEspacio_LanzaArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AsignacionSemanal(
                _validId, _validSesionId, SemanaAcademica.A,
                _validBloqueId, _validEspacioId, Modalidad.Virtual));
        }

        [Fact]
        public void Constructor_IdVacio_LanzaArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AsignacionSemanal(
                Guid.Empty, _validSesionId, SemanaAcademica.A,
                _validBloqueId, _validEspacioId, Modalidad.Presencial));
        }

        [Fact]
        public void Constructor_SesionIdVacio_LanzaArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AsignacionSemanal(
                _validId, Guid.Empty, SemanaAcademica.A,
                _validBloqueId, _validEspacioId, Modalidad.Presencial));
        }

        [Fact]
        public void Constructor_BloqueTiempoIdVacio_LanzaArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new AsignacionSemanal(
                _validId, _validSesionId, SemanaAcademica.A,
                Guid.Empty, _validEspacioId, Modalidad.Presencial));
        }

        [Fact]
        public void SemanaAcademica_TieneDosValores()
        {
            var valores = Enum.GetValues<SemanaAcademica>();
            Assert.Equal(2, valores.Length);
            Assert.Contains(SemanaAcademica.A, valores);
            Assert.Contains(SemanaAcademica.B, valores);
        }
    }
}
