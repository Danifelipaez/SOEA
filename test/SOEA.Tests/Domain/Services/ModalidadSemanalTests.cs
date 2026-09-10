using System;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// El invariante del modelo semanal: una sesión existe en todas las semanas, solo su modalidad
    /// cambia, y solo si alterna. Únicamente la ocupación de AULA depende de la semana — que es la
    /// razón por la que emparejar libera capacidad y no emparejar no libera nada.
    /// </summary>
    public class ModalidadSemanalTests
    {
        private static Sesion Sesion(
            TipoAlternancia alternancia = TipoAlternancia.SinAlternancia,
            Modalidad modalidad = Modalidad.Presencial) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                alternancia, modalidad, 2m, false, false, TipoFlujo.AulaVirtual);

        [Fact]
        public void SinAlternancia_EsPresencialEnAmbasSemanas_YOcupaAulaEnAmbas()
        {
            var s = Sesion();

            Assert.Equal(Modalidad.Presencial, ModalidadSemanal.Derivar(s, SemanaAcademica.A));
            Assert.Equal(Modalidad.Presencial, ModalidadSemanal.Derivar(s, SemanaAcademica.B));
            Assert.Equal(new[] { SemanaAcademica.A, SemanaAcademica.B },
                         ModalidadSemanal.SemanasQueOcupanEspacio(s).ToArray());
        }

        [Fact]
        public void TipoA_SoloOcupaAulaEnSemanaA_YSuFilaCanonicaEsA()
        {
            var s = Sesion(TipoAlternancia.TipoA);

            Assert.Equal(new[] { SemanaAcademica.A }, ModalidadSemanal.SemanasQueOcupanEspacio(s).ToArray());
            Assert.Equal(SemanaAcademica.A, ModalidadSemanal.SemanaCanonica(s));
            Assert.Equal(Modalidad.Presencial, ModalidadSemanal.ModalidadCanonica(s));
        }

        [Fact]
        public void TipoB_SoloOcupaAulaEnSemanaB_YSuFilaCanonicaEsB()
        {
            var s = Sesion(TipoAlternancia.TipoB);

            Assert.Equal(new[] { SemanaAcademica.B }, ModalidadSemanal.SemanasQueOcupanEspacio(s).ToArray());
            Assert.Equal(SemanaAcademica.B, ModalidadSemanal.SemanaCanonica(s));
            Assert.Equal(Modalidad.Presencial, ModalidadSemanal.ModalidadCanonica(s));
        }

        [Fact]
        public void VirtualPura_NoOcupaAulaEnNingunaSemana_YSuFilaCanonicaEsAVirtual()
        {
            var s = Sesion(TipoAlternancia.SinAlternancia, Modalidad.Virtual);

            Assert.Empty(ModalidadSemanal.SemanasQueOcupanEspacio(s));
            Assert.Equal(SemanaAcademica.A, ModalidadSemanal.SemanaCanonica(s));
            Assert.Equal(Modalidad.Virtual, ModalidadSemanal.ModalidadCanonica(s));
        }

        [Fact]
        public void VirtualPuraMarcadaTipoB_SigueSiendoFilaEnSemanaA_NoReservaNada()
        {
            // Dato inconsistente (virtual + TipoB). La fila canónica no puede irse a la semana B:
            // una virtual no reserva aula, así que no hay nada que alternar.
            var s = Sesion(TipoAlternancia.TipoB, Modalidad.Virtual);

            Assert.Equal(SemanaAcademica.A, ModalidadSemanal.SemanaCanonica(s));
            Assert.Empty(ModalidadSemanal.SemanasQueOcupanEspacio(s));
        }

        [Fact]
        public void ParejaTipoATipoB_NoComparteSemanaDeEspacio()
        {
            // Por eso una pareja puede compartir bloque y aula sin que sea un conflicto.
            Assert.False(ModalidadSemanal.CompartenSemanaDeEspacio(
                Sesion(TipoAlternancia.TipoA), Sesion(TipoAlternancia.TipoB)));
        }

        [Fact]
        public void NoAlternante_ComparteSemanaDeEspacioConCualquierPresencial()
        {
            var fija = Sesion();

            Assert.True(ModalidadSemanal.CompartenSemanaDeEspacio(fija, Sesion()));
            Assert.True(ModalidadSemanal.CompartenSemanaDeEspacio(fija, Sesion(TipoAlternancia.TipoA)));
            // El caso que el índice único de BD ya no puede ver: una fila en A contra una en B.
            Assert.True(ModalidadSemanal.CompartenSemanaDeEspacio(fija, Sesion(TipoAlternancia.TipoB)));
        }

        [Fact]
        public void VirtualPura_NuncaComparteSemanaDeEspacio()
        {
            Assert.False(ModalidadSemanal.CompartenSemanaDeEspacio(
                Sesion(TipoAlternancia.SinAlternancia, Modalidad.Virtual), Sesion()));
        }
    }
}
