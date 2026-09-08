using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;
using SOEA.Engine.ConstraintProg;

namespace SOEA.Tests.Engine.ConstraintProg
{
    /// <summary>
    /// <see cref="CpSatOptions.SweepGruposMaximo"/> evita que el barrido de diagnóstico
    /// (<see cref="SweepGruposInfactiblesTests"/>) pague un costo O(N) de solves CP-SAT completos
    /// en runs con muchos grupos candidatos. Cubre el guard `candidatos.Count > SweepGruposMaximo`
    /// (MotorConstraintProgramming.cs:660) y su boundary exacto.
    /// </summary>
    public class SweepGruposMaximoTests
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
        public async Task TopeSuperado_SweepSeOmite()
        {
            // Mismo escenario que SweepGruposInfactiblesTests (2 grupos, mismo espacio fijo,
            // 1 solo bloque) pero con el tope fijado en 1: con 2 candidatos supera el tope
            // (candidatos.Count > SweepGruposMaximo ⇒ 2 > 1) y el barrido debe omitirse.
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true, SweepGruposMaximo = 1 });

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.Null(resultado.GruposResponsablesIds);
            Assert.DoesNotContain("Diagnóstico adicional", resultado.MensajeError);
        }

        [Fact]
        public async Task TopeExacto_SweepSiCorre()
        {
            // Boundary: con candidatos.Count == SweepGruposMaximo (2 == 2), la comparación
            // estrictamente-mayor NO debe omitir el barrido — verifica que el guard no tiene
            // un off-by-one que excluya el caso límite.
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(
                NullLogger<MotorConstraintProgramming>.Instance,
                new CpSatOptions { SweepGrupos = true, SweepGruposMaximo = 2 });

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.NotNull(resultado.GruposResponsablesIds);
            Assert.Contains(grupoA.Id, resultado.GruposResponsablesIds!);
            Assert.Contains(grupoB.Id, resultado.GruposResponsablesIds!);
        }
    }
}
