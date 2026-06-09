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

        // Sesión presencial con alternancia (presencial una semana, virtual la otra).
        private static Sesion CrearSesionPresencial(
            Guid docenteId, TipoAlternancia alternancia, decimal duracion = 1m, Guid? espacioId = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), espacioId, null,
                alternancia, Modalidad.Presencial, duracion, false, false);

        private static Espacio CrearLaboratorio(string nombre = "Lab") =>
            new(Guid.NewGuid(), nombre, TipoEspacio.Laboratorio, 30);

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

        // Sesión de 2h con sólo 1 bloque disponible al final del día → infactible.
        // El último bloque del día no debe poder ser start de un span de 2h.
        [Fact]
        public async Task Sesion2h_DocenteSoloUnBloque_RetornaInfactible()
        {
            // 1 sólo bloque disponible (último del día efectivo): no cabe sesión de 2h
            var bloques = CrearBloques(1);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var sesion = CrearSesion(docente.Id, 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.False(resultado.EsFactible);
        }

        // Dos sesiones del mismo docente con duraciones distintas no pueden solapar
        // en ninguno de los bloques cubiertos por sus spans (NoOverlap por docente).
        [Fact]
        public async Task DosSesionesMismoDocente_NoSolapan_RetornaFactible()
        {
            var bloques = CrearBloques(5); // 7:00 a 12:00
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var a = CrearSesion(docente.Id, 2m);
            var b = CrearSesion(docente.Id, 1m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { a, b }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.True(resultado.EsFactible);
            // Cada sesión produce dos AsignacionSemanal (A/B). El no-solapamiento se verifica
            // por semana; tomamos la Semana A.
            var asignacionesA = resultado.Asignaciones.Where(x => x.Semana == SemanaAcademica.A).ToList();
            var bloqueA = bloques.First(bl => bl.Id == asignacionesA.First(s => s.SesionId == a.Id).BloqueTiempoId);
            var bloqueB = bloques.First(bl => bl.Id == asignacionesA.First(s => s.SesionId == b.Id).BloqueTiempoId);

            // Verificación de no-solapamiento: end de A <= start de B  ó  end de B <= start de A
            var startA = bloqueA.HoraInicio; var endA = bloqueA.HoraInicio.AddHours(2);
            var startB = bloqueB.HoraInicio; var endB = bloqueB.HoraInicio.AddHours(1);
            var noSolapan = endA <= startB || endB <= startA;
            Assert.True(noSolapan, $"Sesiones solapan: A[{startA:HH\\:mm}-{endA:HH\\:mm}] B[{startB:HH\\:mm}-{endB:HH\\:mm}]");
        }

        // ── Bi-semanal (Incremento 1) ──────────────────────────────────────────────

        // Cada sesión factible produce exactamente dos AsignacionSemanal (Semana A y B).
        [Fact]
        public async Task SesionFactible_ProduceDosAsignaciones_UnaPorSemana()
        {
            var bloques = CrearBloques(3);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var sesion = CrearSesion(docente.Id, 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.True(resultado.EsFactible);
            var delaSesion = resultado.Asignaciones.Where(a => a.SesionId == sesion.Id).ToList();
            Assert.Equal(2, delaSesion.Count);
            Assert.Single(delaSesion, a => a.Semana == SemanaAcademica.A);
            Assert.Single(delaSesion, a => a.Semana == SemanaAcademica.B);
        }

        // Regla 9: para alternancia (TipoA/TipoB) la franja es la misma en ambas semanas
        // (la virtual hereda la franja de la presencial mediante start[A] == start[B]).
        [Fact]
        public async Task SesionTipoA_MismaFranjaEnAmbasSemanas_YModalidadDerivada()
        {
            var bloques = CrearBloques(4);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var lab = CrearLaboratorio();
            var sesion = CrearSesionPresencial(docente.Id, TipoAlternancia.TipoA, 1m, lab.Id);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { lab }, new[] { docente });

            Assert.True(resultado.EsFactible);
            var a = resultado.Asignaciones.Single(x => x.SesionId == sesion.Id && x.Semana == SemanaAcademica.A);
            var b = resultado.Asignaciones.Single(x => x.SesionId == sesion.Id && x.Semana == SemanaAcademica.B);

            // Misma franja en ambas semanas (regla 9).
            Assert.Equal(a.BloqueTiempoId, b.BloqueTiempoId);

            // TipoA: presencial en A (con espacio), virtual en B (sin espacio).
            Assert.Equal(Modalidad.Presencial, a.Modalidad);
            Assert.NotNull(a.EspacioId);
            Assert.Equal(Modalidad.Virtual, b.Modalidad);
            Assert.Null(b.EspacioId);
        }

        // El valor central del modelo bi-semanal: un TipoA y un TipoB pueden compartir el
        // MISMO laboratorio porque cada uno ocupa el espacio en semanas distintas.
        [Fact]
        public async Task TipoA_y_TipoB_CompartenLaboratorio_EnSemanasDistintas_RetornaFactible()
        {
            // Un solo bloque, un solo laboratorio: sin la dimensión semana sería infactible
            // (dos sesiones presenciales en el mismo slot/espacio). Con A/B es factible.
            var bloques = CrearBloques(1);
            var docenteA = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var docenteB = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var lab = CrearLaboratorio();

            var sesionA = CrearSesionPresencial(docenteA.Id, TipoAlternancia.TipoA, 1m, lab.Id);
            var sesionB = CrearSesionPresencial(docenteB.Id, TipoAlternancia.TipoB, 1m, lab.Id);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesionA, sesionB }, bloques, new[] { lab }, new[] { docenteA, docenteB });

            Assert.True(resultado.EsFactible);

            // En Semana A: TipoA es presencial en el lab; TipoB es virtual (no ocupa lab).
            var aPresenciales = resultado.Asignaciones
                .Where(x => x.Semana == SemanaAcademica.A && x.Modalidad == Modalidad.Presencial)
                .ToList();
            Assert.Single(aPresenciales);
            Assert.Equal(sesionA.Id, aPresenciales[0].SesionId);

            // En Semana B: TipoB es presencial en el lab; TipoA es virtual.
            var bPresenciales = resultado.Asignaciones
                .Where(x => x.Semana == SemanaAcademica.B && x.Modalidad == Modalidad.Presencial)
                .ToList();
            Assert.Single(bPresenciales);
            Assert.Equal(sesionB.Id, bPresenciales[0].SesionId);
        }

        // HC-S04 / regla 9: toda asignación virtual debe tener EspacioId == null.
        [Fact]
        public async Task AsignacionesVirtuales_NoTienenEspacio()
        {
            var bloques = CrearBloques(3);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var sesion = CrearSesion(docente.Id, 2m); // Modalidad.Virtual en ambas semanas

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { CrearLaboratorio() }, new[] { docente });

            Assert.True(resultado.EsFactible);
            Assert.All(resultado.Asignaciones, a =>
            {
                Assert.Equal(Modalidad.Virtual, a.Modalidad);
                Assert.Null(a.EspacioId);
            });
        }
    }
}
