using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Engine.ConstraintProg;

namespace SOEA.Tests.Engine.ConstraintProg
{
    public class MotorConstraintProgrammingTests
    {
        private static readonly MotorConstraintProgramming Motor =
            new(NullLogger<MotorConstraintProgramming>.Instance);

        private static Docente CrearDocente(decimal maxHoras = 40m, IEnumerable<BloqueTiempo>? bloquesDisponibles = null)
        {
            var id = Guid.NewGuid();
            var docente = new Docente(id, "Docente", "", $"docente-{id}@soea.edu", maxHoras,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
            foreach (var b in bloquesDisponibles ?? Enumerable.Empty<BloqueTiempo>())
                docente.AgregarBloqueDisponibilidad(b);
            return docente;
        }

        private static Sesion CrearSesion(Guid docenteId, decimal duracion = 2m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, duracion, false, false);

        private static List<BloqueTiempo> CrearBloques(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();

        // HC-I03: docente with zero available blocks must be rejected before CP-SAT runs
        [Fact]
        public async Task Docente_SinBloques_RetornaInfactible()
        {
            var bloques = CrearBloques(2);
            var docente = CrearDocente(bloquesDisponibles: null);
            var sesion = CrearSesion(docente.Id);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.False(resultado.EsFactible);
            Assert.Contains("no tiene disponibilidad", resultado.MensajeError, StringComparison.OrdinalIgnoreCase);
        }

        // HC-I03: total session hours exceeding docente max must be rejected
        [Fact]
        public async Task Docente_MaxHorasExcedidas_RetornaInfactible()
        {
            var bloques = CrearBloques(5);
            var docente = CrearDocente(maxHoras: 4m, bloquesDisponibles: bloques);
            // 3 sessions × 2h = 6h > 4h max
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(docente.Id, 2m)).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.False(resultado.EsFactible);
            Assert.Contains("excede su máximo", resultado.MensajeError, StringComparison.OrdinalIgnoreCase);
        }

        // HC-I02 + HC-I03: docente with sufficient blocks and 1 session must yield a feasible assignment
        [Fact]
        public async Task Docente_ConBloquesSuficientes_SesionVirtual_RetornaFactible()
        {
            var bloques = CrearBloques(3);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var sesion = CrearSesion(docente.Id, 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.True(resultado.EsFactible);
        }

        // HC-I03: sessions exceeding available block count must be rejected (more sessions than slots)
        [Fact]
        public async Task Docente_MasSesioneQuesBloques_RetornaInfactible()
        {
            var bloques = CrearBloques(2);
            var docente = CrearDocente(maxHoras: 40m, bloquesDisponibles: bloques); // 2 blocks
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(docente.Id, 1m)).ToList(); // 3 sessions

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.False(resultado.EsFactible);
        }
    }
}
