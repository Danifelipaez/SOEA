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
    /// Un timeout real del solver (CpSolverStatus.Unknown) se reporta como
    /// MotivoInfactibilidad.Timeout y hace `return` (MotorConstraintProgramming.cs:605-612)
    /// ANTES de llegar al bloque `if (permitirSweep)` — el barrido de diagnóstico nunca corre
    /// para un timeout, solo para una infactibilidad real (status Infeasible/otro). Con
    /// TimeoutSegundos=0 el solver no tiene presupuesto de tiempo y devuelve Unknown de forma
    /// determinista, sin depender de que el problema sea genuinamente difícil de resolver.
    /// </summary>
    public class SweepGruposTimeoutTests
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
        public async Task TimeoutAgotado_DevuelveMotivoTimeout_SinDispararElBarrido()
        {
            // Mismo escenario infactible que SweepGruposInfactiblesTests, pero con
            // TimeoutSegundos=0: el solver no tiene presupuesto de tiempo y termina Unknown
            // (no Infeasible), sin importar si el modelo es genuinamente irresoluble o no.
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true, TimeoutSegundos = 0, NumWorkers = 1 });

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Timeout, resultado.Motivo);
            Assert.Null(resultado.GruposResponsablesIds);
            Assert.DoesNotContain("Diagnóstico adicional", resultado.MensajeError);
        }
    }
}
