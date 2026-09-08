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
    /// El barrido de diagnóstico (<see cref="SweepGruposInfactiblesTests"/>) solo nombraba a los
    /// grupos responsables dentro del texto libre de MensajeError — un frontend no puede parsear
    /// una oración humana de forma confiable para saber a qué grupo pintarle un aviso. Este archivo
    /// cubre el campo estructurado <see cref="ResultadoFactibilidad.GruposResponsablesIds"/> que
    /// expone los mismos Ids ya calculados por el barrido, sin tocar el mensaje existente.
    /// </summary>
    public class SweepGruposResponsablesIdsTests
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
        public async Task SweepActivado_ExponeIdsDeGruposResponsablesEnCampoEstructurado()
        {
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);

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
            Assert.NotNull(resultado.GruposResponsablesIds);
            Assert.Contains(grupoA.Id, resultado.GruposResponsablesIds!);
            Assert.Contains(grupoB.Id, resultado.GruposResponsablesIds!);
        }

        [Fact]
        public async Task SweepDesactivado_GruposResponsablesIdsEsNull()
        {
            var bloques = CrearBloques(1);
            var espacioFijo = new Espacio(Guid.NewGuid(), "Salón fijo", TipoEspacio.Salon, 30);
            var otroSalon = new Espacio(Guid.NewGuid(), "Salón libre", TipoEspacio.Salon, 30);
            var grupoA = GrupoConEspacioFijo(espacioFijo.Id);
            var grupoB = GrupoConEspacioFijo(espacioFijo.Id);
            var sesionA = CrearSesionPresencial(grupoA.Id, 1m);
            var sesionB = CrearSesionPresencial(grupoB.Id, 1m);

            var motor = new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance);

            var resultado = await motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { espacioFijo, otroSalon },
                new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.Null(resultado.GruposResponsablesIds);
        }
    }
}
