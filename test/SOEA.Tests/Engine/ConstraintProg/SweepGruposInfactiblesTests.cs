using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;
using SOEA.Engine.ConstraintProg;

namespace SOEA.Tests.Engine.ConstraintProg
{
    /// <summary>
    /// Barrido opcional (CpSatOptions.SweepGrupos): cuando CP-SAT confirma INFEASIBLE sin que
    /// ningún pre-check estructural lo haya explicado (el catch-all final, MotivoInfactibilidad.Otro),
    /// se reintenta el solve una vez por grupo excluyéndolo, para nombrar cuáles son responsables —
    /// cubre causas que la disponibilidad de un solo grupo (HC-G01 agregada) no puede ver, como
    /// dos grupos compitiendo por el mismo espacio fijo (HC-S05).
    /// </summary>
    public class SweepGruposInfactiblesTests
    {
        private static List<BloqueTiempo> CrearBloques(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();

        private static Grupo GrupoConEspacioFijo(Guid espacioFijoId, int estudiantes = 20)
        {
            var grupo = new Grupo(Guid.NewGuid(), $"Grupo-{Guid.NewGuid():N}".Substring(0, 12), Guid.NewGuid(), estudiantes);
            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.TeoriaPresencial, espacioFijoId, TipoEspacio.Salon, 1)
            });
            return grupo;
        }

        private static Sesion CrearSesionPresencial(Guid grupoId, decimal duracion = 1m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public async Task DosGruposCompitenPorElMismoEspacioFijo_SweepLosNombraAAmbos()
        {
            // Grilla de 1 solo bloque: dos sesiones que exigen el MISMO espacio fijo no pueden
            // evitar solaparse (solo hay un momento posible), aunque cada grupo por separado
            // (sin el otro) sí cabría perfectamente — HC-G01 agregada no ve esto (ninguno declara
            // disponibilidad), y el chequeo de capacidad por tipo tampoco (hay 2 salones en total,
            // la demanda de 2h cabe en 2h de capacidad agregada) — solo CP-SAT lo prueba.
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30); // nunca elegible (RequisitoEspacio fija el otro)

            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true });

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            // Dos grupos peleando por el mismo espacio fijo ES un problema de aulas: el motivo
            // dice QUÉ hacer (emparejar / añadir aulas) y el barrido dice A QUIÉN mirar.
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
            Assert.Contains(grupoA.Nombre, resultado.MensajeError);
            Assert.Contains(grupoB.Nombre, resultado.MensajeError);
        }

        [Fact]
        public async Task SweepDesactivadoPorDefault_NoNombraGrupos()
        {
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            // Motor sin CpSatOptions explícito ⇒ SweepGrupos default false.
            var motor = new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance);

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
            Assert.DoesNotContain(grupoA.Nombre, resultado.MensajeError);
            Assert.DoesNotContain(grupoB.Nombre, resultado.MensajeError);
        }

        // El barrido no debe correr cuando un pre-check estructural ya explicó la causa —
        // reusa el escenario de HC-G01 agregada (un grupo sobrecargado consigo mismo).
        [Fact]
        public async Task SweepNoCorreCuandoUnPreCheckYaExplicoLaCausa()
        {
            var bloques = CrearBloques(5);
            var grupo = new Grupo(Guid.NewGuid(), $"Grupo-{Guid.NewGuid():N}".Substring(0, 12), Guid.NewGuid(), 20);
            grupo.ActualizarDisponibilidadUi(
                "{\"lunes\":{\"noDisponible\":false,\"tipo\":\"Franja específica\",\"desde\":\"07:00\",\"hasta\":\"09:00\"}}");
            var sesiones = Enumerable.Range(0, 3).Select(_ =>
                new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupo.Id,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false)).ToList();

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true });

            var resultado = await motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { grupo });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.FranjaGrupo, resultado.Motivo);
            Assert.DoesNotContain("Diagnóstico adicional", resultado.MensajeError);
        }
    }
}
