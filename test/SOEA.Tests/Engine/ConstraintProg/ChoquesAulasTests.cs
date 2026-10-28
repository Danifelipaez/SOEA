using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;

namespace SOEA.Tests.Engine.ConstraintProg
{
    /// <summary>
    /// Caso de producción (2026-10-28): con 2 salones y cada grupo con una franja exacta, el
    /// miércoles de 08:00 a 09:00 coincidían FUNDAMENTOS QUIMICA (06–09), BIOQUIMICA (08–10) y
    /// QUIMICA GENERAL (08–10). La Fase 2 respondía "No alcanzan las aulas" sin decir dónde.
    /// </summary>
    public class ChoquesAulasTests
    {
        private static readonly MotorConstraintProgramming Motor =
            new(NullLogger<MotorConstraintProgramming>.Instance);

        // Miércoles 06:00–12:00, bloques de 1 h.
        private static List<BloqueTiempo> Miercoles() =>
            Enumerable.Range(0, 6)
                .Select(i => new BloqueTiempo(Guid.NewGuid(), DiaDeSemana.Miercoles,
                    new TimeOnly(6 + i, 0), new TimeOnly(7 + i, 0)))
                .ToList();

        private static Espacio Salon(string nombre) => new(Guid.NewGuid(), nombre, TipoEspacio.Salon, 30);

        private static Grupo Grupo(string nombre, string desde, string hasta)
        {
            var grupo = new Grupo(Guid.NewGuid(), nombre, Guid.NewGuid(), 30);
            grupo.ActualizarDisponibilidadUi(
                $"{{\"miercoles\":{{\"noDisponible\":false,\"tipo\":\"Franja específica\",\"desde\":\"{desde}\",\"hasta\":\"{hasta}\"}}}}");
            return grupo;
        }

        private static Sesion Teoria(Grupo grupo, decimal horas) =>
            new(Guid.NewGuid(), grupo.AsignaturaId, null, Guid.NewGuid(), null, grupo.Id,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, horas, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public async Task TresClasesObligadasALaMismaHoraConDosSalones_NombraLaFranjaYLosGrupos()
        {
            var fundamentos = Grupo("FUNDAMENTOS QUIMICA - Grupo 1", "06:00", "09:00");
            var bioquimica = Grupo("BIOQUIMICA - Grupo 1", "08:00", "10:00");
            var general = Grupo("QUIMICA GENERAL - Grupo 1", "08:00", "10:00");
            var grupos = new[] { fundamentos, bioquimica, general };
            var sesiones = new[] { Teoria(fundamentos, 3m), Teoria(bioquimica, 2m), Teoria(general, 2m) };

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, Miercoles(), new[] { Salon("QUI"), Salon("BIO") }, grupos);

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo); // emparejar sigue siendo una salida
            Assert.Contains("miércoles de 08:00 a 09:00 coinciden 3 clases y solo hay 2 aulas", resultado.MensajeError);
            foreach (var g in grupos) Assert.Contains(g.Nombre, resultado.MensajeError);
            Assert.Equal(grupos.Select(g => g.Id).OrderBy(x => x), resultado.GruposResponsablesIds!.OrderBy(x => x));
        }

        [Fact]
        public async Task AmpliandoLaDisponibilidadDeUno_Genera()
        {
            var fundamentos = Grupo("FUNDAMENTOS QUIMICA - Grupo 1", "06:00", "09:00");
            var bioquimica = Grupo("BIOQUIMICA - Grupo 1", "08:00", "12:00"); // puede pasar a 10–12
            var general = Grupo("QUIMICA GENERAL - Grupo 1", "08:00", "10:00");
            var sesiones = new[] { Teoria(fundamentos, 3m), Teoria(bioquimica, 2m), Teoria(general, 2m) };

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, Miercoles(), new[] { Salon("QUI"), Salon("BIO") }, new[] { fundamentos, bioquimica, general });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }

        [Fact]
        public async Task EmparejandoDosDeLasQueChocan_Genera()
        {
            var fundamentos = Grupo("FUNDAMENTOS QUIMICA - Grupo 1", "06:00", "09:00");
            var bioquimica = Grupo("BIOQUIMICA - Grupo 1", "08:00", "10:00");
            var general = Grupo("QUIMICA GENERAL - Grupo 1", "08:00", "10:00");
            var sBio = Teoria(bioquimica, 2m); var sGen = Teoria(general, 2m);
            var pareja = Guid.NewGuid();
            sBio.AplicarAlternancia(TipoAlternancia.TipoA, cedidaPorSaturacion: true, parejaAlternanciaId: pareja);
            sGen.AplicarAlternancia(TipoAlternancia.TipoB, cedidaPorSaturacion: true, parejaAlternanciaId: pareja);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { Teoria(fundamentos, 3m), sBio, sGen }, Miercoles(),
                new[] { Salon("QUI"), Salon("BIO") }, new[] { fundamentos, bioquimica, general });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }
    }
}
