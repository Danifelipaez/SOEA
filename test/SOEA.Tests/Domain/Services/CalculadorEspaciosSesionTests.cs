using System;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;
using Xunit;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// Fuente única de candidatos de espacio (HC-S03/HC-S05) — antes sólo se probaba indirectamente
    /// vía CP-SAT/GA/validador (M9 del análisis). Estos tests aíslan CumpleTipo/Candidatos, con foco
    /// en M6: RequisitoEspacio.TipoEspacio nullable — un requisito sin EspacioId NI TipoEspacio debe
    /// caer a la regla por defecto, no congelarse en "Salon" (default del enum antes del fix).
    /// </summary>
    public class CalculadorEspaciosSesionTests
    {
        private static Espacio Espacio(TipoEspacio tipo, int capacidad = 30) =>
            new(Guid.NewGuid(), tipo.ToString(), tipo, capacidad);

        private static Sesion SesionDe(TipoFlujo tipoFlujo, Modalidad modalidad = Modalidad.Presencial) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, modalidad, 1m, false, false, tipoFlujo: tipoFlujo);

        [Fact]
        public void CumpleTipo_SinRequisito_LaboratorioExigeLaboratorio()
        {
            var lab = Espacio(TipoEspacio.Laboratorio);
            var salon = Espacio(TipoEspacio.Salon);

            Assert.True(CalculadorEspaciosSesion.CumpleTipo(lab, TipoSesion.Laboratorio, null));
            Assert.False(CalculadorEspaciosSesion.CumpleTipo(salon, TipoSesion.Laboratorio, null));
        }

        [Fact]
        public void CumpleTipo_SinRequisito_TeoriaPresencialExcluyeLaboratorio()
        {
            var lab = Espacio(TipoEspacio.Laboratorio);
            var salon = Espacio(TipoEspacio.Salon);
            var auditorio = Espacio(TipoEspacio.Auditorio);

            Assert.False(CalculadorEspaciosSesion.CumpleTipo(lab, TipoSesion.TeoriaPresencial, null));
            Assert.True(CalculadorEspaciosSesion.CumpleTipo(salon, TipoSesion.TeoriaPresencial, null));
            Assert.True(CalculadorEspaciosSesion.CumpleTipo(auditorio, TipoSesion.TeoriaPresencial, null));
        }

        [Fact]
        public void CumpleTipo_RequisitoConEspacioFijo_SoloEseEspacioCumple()
        {
            var fijo = Espacio(TipoEspacio.Salon);
            var otro = Espacio(TipoEspacio.Salon);
            var requisito = new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio: null, Sesiones: 1);

            Assert.True(CalculadorEspaciosSesion.CumpleTipo(fijo, TipoSesion.TeoriaPresencial, requisito));
            Assert.False(CalculadorEspaciosSesion.CumpleTipo(otro, TipoSesion.TeoriaPresencial, requisito));
        }

        [Fact]
        public void CumpleTipo_RequisitoConTipoExplicito_SoloEseTipoCumple()
        {
            // El requisito pide Auditorio explícitamente para una TeoriaPresencial — la regla por
            // defecto habría aceptado cualquier no-laboratorio (incluido Salon); el requisito la
            // restringe más.
            var auditorio = Espacio(TipoEspacio.Auditorio);
            var salon = Espacio(TipoEspacio.Salon);
            var requisito = new RequisitoEspacio(TipoSesion.TeoriaPresencial, null, TipoEspacio.Auditorio, Sesiones: 1);

            Assert.True(CalculadorEspaciosSesion.CumpleTipo(auditorio, TipoSesion.TeoriaPresencial, requisito));
            Assert.False(CalculadorEspaciosSesion.CumpleTipo(salon, TipoSesion.TeoriaPresencial, requisito));
        }

        // M6: el hallazgo central del fix. Antes RequisitoEspacio.TipoEspacio no era nullable y
        // TipoEspacio.Salon == 0, así que un requisito con EspacioId y TipoEspacio ambos ausentes
        // del JSON (p. ej. una entrada creada sólo para declarar Sesiones) deserializaba TipoEspacio
        // a Salon y forzaba silenciosamente "sólo salón" — una sesión de laboratorio sin type
        // explícito quedaba sin ningún candidato. Con TipoEspacio? nullable, ese mismo requisito cae
        // a la regla por defecto de TipoSesion, igual que si no hubiera requisito.
        [Theory]
        [InlineData(TipoSesion.Laboratorio, TipoEspacio.Laboratorio, true)]
        [InlineData(TipoSesion.Laboratorio, TipoEspacio.Salon, false)]
        [InlineData(TipoSesion.TeoriaPresencial, TipoEspacio.Salon, true)]
        [InlineData(TipoSesion.TeoriaPresencial, TipoEspacio.Laboratorio, false)]
        public void CumpleTipo_RequisitoSinEspacioNiTipo_CaeALaReglaPorDefecto(
            TipoSesion tipoSesion, TipoEspacio tipoEspacioProbado, bool esperado)
        {
            var espacio = Espacio(tipoEspacioProbado);
            var requisitoVacio = new RequisitoEspacio(tipoSesion, EspacioId: null, TipoEspacio: null, Sesiones: 1);

            Assert.Equal(esperado, CalculadorEspaciosSesion.CumpleTipo(espacio, tipoSesion, requisitoVacio));
            // Mismo resultado que sin requisito en absoluto — un requisito "vacío" no debe cambiar nada.
            Assert.Equal(
                CalculadorEspaciosSesion.CumpleTipo(espacio, tipoSesion, null),
                CalculadorEspaciosSesion.CumpleTipo(espacio, tipoSesion, requisitoVacio));
        }

        [Fact]
        public void Candidatos_ConEspacioFijoPresenteEnLaLista_DevuelveSoloEse()
        {
            var fijo = Espacio(TipoEspacio.Salon);
            var otros = new[] { Espacio(TipoEspacio.Salon), Espacio(TipoEspacio.Salon) };
            var espacios = new[] { otros[0], fijo, otros[1] };
            var sesion = SesionDe(TipoFlujo.AulaVirtual);
            var requisito = new RequisitoEspacio(TipoSesion.TeoriaPresencial, fijo.Id, null, 1);

            var candidatos = CalculadorEspaciosSesion.Candidatos(sesion, espacios, requisito).ToList();

            Assert.Single(candidatos);
            Assert.Equal(fijo.Id, espacios[candidatos[0]].Id);
        }

        [Fact]
        public void Candidatos_SinRequisito_FiltraPorTipoPorDefecto()
        {
            var lab = Espacio(TipoEspacio.Laboratorio);
            var salon = Espacio(TipoEspacio.Salon);
            var auditorio = Espacio(TipoEspacio.Auditorio);
            var espacios = new[] { lab, salon, auditorio };
            var sesion = SesionDe(TipoFlujo.AulaVirtual); // TeoriaPresencial

            var candidatos = CalculadorEspaciosSesion.Candidatos(sesion, espacios, null)
                .Select(i => espacios[i].Id).ToList();

            Assert.Contains(salon.Id, candidatos);
            Assert.Contains(auditorio.Id, candidatos);
            Assert.DoesNotContain(lab.Id, candidatos);
        }

        [Fact]
        public void TipoSesionDe_DerivaCorrectamenteDeTipoFlujoYModalidad()
        {
            Assert.Equal(TipoSesion.Laboratorio, CalculadorEspaciosSesion.TipoSesionDe(SesionDe(TipoFlujo.Laboratorio)));
            Assert.Equal(TipoSesion.TeoriaPresencial, CalculadorEspaciosSesion.TipoSesionDe(SesionDe(TipoFlujo.AulaVirtual, Modalidad.Presencial)));
            Assert.Equal(TipoSesion.TeoriaVirtual, CalculadorEspaciosSesion.TipoSesionDe(SesionDe(TipoFlujo.AulaVirtual, Modalidad.Virtual)));
        }
    }
}
