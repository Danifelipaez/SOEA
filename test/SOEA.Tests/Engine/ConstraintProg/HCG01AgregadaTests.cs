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
    /// HC-G01 agregada: antes, la Fase 2 solo probaba infactibilidad de disponibilidad de grupo
    /// sesión por sesión (¿cabe ESTA sola en la ventana?), nunca la suma de todas las sesiones no
    /// fijas de un grupo contra el total de bloques que su disponibilidad declarada permite —
    /// aunque HC-C01 (NoOverlap por grupo) garantiza que, si la suma excede el total permitido, el
    /// modelo es infactible con certeza. Esa causa caía en el catch-all genérico sin nombrar a
    /// nadie (ver MotorConstraintProgrammingTests.Cohorte_*_RetornaInfactible, que sí siguen
    /// devolviendo Otro cuando no se pasa ningún Grupo).
    /// </summary>
    public class HCG01AgregadaTests
    {
        private static readonly MotorConstraintProgramming Motor =
            new(NullLogger<MotorConstraintProgramming>.Instance);

        private static Sesion CrearSesion(Guid grupoId, decimal duracion = 1m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, duracion, false, false);

        private static List<BloqueTiempo> CrearBloques(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();

        private static Grupo GrupoConVentana(string dia, string desde, string hasta)
        {
            var grupo = new Grupo(Guid.NewGuid(), $"Grupo-{Guid.NewGuid():N}".Substring(0, 12), Guid.NewGuid(), 20);
            grupo.ActualizarDisponibilidadUi(
                $"{{\"{dia}\":{{\"noDisponible\":false,\"tipo\":\"Franja específica\",\"desde\":\"{desde}\",\"hasta\":\"{hasta}\"}}}}");
            return grupo;
        }

        [Fact]
        public async Task GrupoConMasCargaQueVentana_RetornaInfactibleYNombraElGrupo()
        {
            var bloques = CrearBloques(5); // lunes 07:00–12:00, 1h cada uno
            // Ventana declarada: lunes 07:00–09:00 → solo 2 bloques permitidos (0 y 1).
            var grupo = GrupoConVentana("lunes", "07:00", "09:00");
            // 3 sesiones de 1h no fijas del grupo ⇒ 3 bloques requeridos > 2 permitidos.
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(grupo.Id, 1m)).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { grupo });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.FranjaGrupo, resultado.Motivo);
            Assert.Contains(grupo.Nombre, resultado.MensajeError);
            Assert.Contains("3 bloque", resultado.MensajeError);
        }

        [Fact]
        public async Task DosGruposSobrecargados_NombraAmbosEnElMensaje()
        {
            var bloques = CrearBloques(5);
            var grupoA = GrupoConVentana("lunes", "07:00", "09:00"); // 2 bloques permitidos
            var grupoB = GrupoConVentana("lunes", "07:00", "08:00"); // 1 bloque permitido
            var sesiones = new List<Sesion>();
            sesiones.AddRange(Enumerable.Range(0, 3).Select(_ => CrearSesion(grupoA.Id, 1m))); // 3 > 2
            sesiones.AddRange(Enumerable.Range(0, 2).Select(_ => CrearSesion(grupoB.Id, 1m))); // 2 > 1

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { grupoA, grupoB });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.FranjaGrupo, resultado.Motivo);
            Assert.Contains(grupoA.Nombre, resultado.MensajeError);
            Assert.Contains(grupoB.Nombre, resultado.MensajeError);
        }

        [Fact]
        public async Task CargaExactaAlLimite_NoEsFalsoPositivo()
        {
            var bloques = CrearBloques(5);
            // Ventana: lunes 07:00–09:00 → exactamente 2 bloques permitidos (0 y 1, contiguos).
            var grupo = GrupoConVentana("lunes", "07:00", "09:00");
            // 2 sesiones de 1h ⇒ exactamente 2 bloques requeridos == 2 permitidos.
            var sesiones = Enumerable.Range(0, 2).Select(_ => CrearSesion(grupo.Id, 1m)).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { grupo });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }

        [Fact]
        public async Task UnaSesionQueCabeExactoEnSuVentana_EsFactible()
        {
            var bloques = CrearBloques(5);
            var grupo = GrupoConVentana("lunes", "07:00", "09:00"); // 2 bloques permitidos (0,1)
            // Una sola sesión de 2h: agregada 2 == 2 (no dispara el chequeo agregado), y cabe como
            // span contiguo {0,1} gracias a la corrección de span-completo (Paso 0).
            var sesionQueCabe = CrearSesion(grupo.Id, 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesionQueCabe }, bloques, Enumerable.Empty<Espacio>(), new[] { grupo });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }

        // Confirma que el chequeo agregado nuevo es no-op para runs sin ningún Grupo —
        // bloquesPermitidosPorGrupo queda vacío y el nuevo loop no encuentra nada que agrupar.
        [Fact]
        public async Task SinGrupos_NoInterfiereConElCatchAllExistente()
        {
            var bloques = CrearBloques(5);
            var cohorte = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(cohorte, 2m)).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>());

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Otro, resultado.Motivo);
        }
    }
}
