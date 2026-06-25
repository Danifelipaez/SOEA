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

        // CR-08: el eje de no-solapamiento es la cohorte (GrupoId); el docente queda fuera del
        // pipeline. El primer parámetro es la cohorte: sesiones que la comparten se serializan,
        // sesiones con cohortes distintas son independientes.
        private static Sesion CrearSesion(Guid grupoId, decimal duracion = 2m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, duracion, false, false);

        // Sesión presencial con alternancia (presencial una semana, virtual la otra).
        private static Sesion CrearSesionPresencial(
            Guid grupoId, TipoAlternancia alternancia, decimal duracion = 1m, Guid? espacioId = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), espacioId, grupoId,
                alternancia, Modalidad.Presencial, duracion, false, false);

        private static Espacio CrearLaboratorio(string nombre = "Lab") =>
            new(Guid.NewGuid(), nombre, TipoEspacio.Laboratorio, 30);

        private static List<BloqueTiempo> CrearBloques(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Lunes,
                    new TimeOnly(7 + i, 0), new TimeOnly(8 + i, 0)))
                .ToList();

        // CR-08 / degradación HC-I02: la disponibilidad docente ya NO es hard constraint de
        // generación. Un docente sin bloques disponibles se agenda igual (cabe en la grilla).
        [Fact]
        public async Task Docente_SinDisponibilidad_SeAgendaIgual()
        {
            var bloques = CrearBloques(2);
            var docente = CrearDocente(bloquesDisponibles: null);
            var sesion = CrearSesion(docente.Id);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.True(resultado.EsFactible);
        }

        // HC-C01 (serialización de cohorte): las sesiones de un grupo no pueden solaparse. Tres
        // sesiones de 2h del mismo grupo necesitan 6 bloques disjuntos; en una grilla de 5 no caben.
        [Fact]
        public async Task Cohorte_NoCabeEnLaGrilla_RetornaInfactible()
        {
            var bloques = CrearBloques(5);
            var cohorte = Guid.NewGuid();
            // 3 sesiones × 2h del MISMO grupo = 6 bloques disjuntos necesarios > 5 disponibles.
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(cohorte, 2m)).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), Enumerable.Empty<Docente>());

            Assert.False(resultado.EsFactible);
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

        // HC-C01 (NoOverlap por cohorte): 3 sesiones de 1h del mismo grupo no caben en una grilla
        // de solo 2 bloques sin solaparse → infactible por serialización de cohorte.
        [Fact]
        public async Task Cohorte_MasSesionesQueBloques_RetornaInfactible()
        {
            var bloques = CrearBloques(2);
            var cohorte = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 3).Select(_ => CrearSesion(cohorte, 1m)).ToList(); // 3 sessions

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(), Enumerable.Empty<Docente>());

            Assert.False(resultado.EsFactible);
        }

        // Sesión de 2h en una grilla de un solo bloque → infactible por estructura: no hay dos
        // bloques contiguos donde colocar el span de 2h (no por disponibilidad, ya degradada).
        [Fact]
        public async Task Sesion2h_GrillaDeUnBloque_RetornaInfactible()
        {
            // Grilla de 1 solo bloque: no cabe una sesión de 2h
            var bloques = CrearBloques(1);
            var docente = CrearDocente(maxHoras: 20m, bloquesDisponibles: bloques);
            var sesion = CrearSesion(docente.Id, 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), new[] { docente });

            Assert.False(resultado.EsFactible);
        }

        // Dos sesiones de la misma cohorte con duraciones distintas no pueden solapar
        // en ninguno de los bloques cubiertos por sus spans (NoOverlap por grupo — HC-C01).
        [Fact]
        public async Task DosSesionesMismaCohorte_NoSolapan_RetornaFactible()
        {
            var bloques = CrearBloques(5); // 7:00 a 12:00
            var cohorte = Guid.NewGuid();
            var a = CrearSesion(cohorte, 2m);
            var b = CrearSesion(cohorte, 1m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { a, b }, bloques, Enumerable.Empty<Espacio>(), Enumerable.Empty<Docente>());

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

        // ── HC-CAP: aforo del espacio >= estudiantes del grupo ──────────────────────

        // El único espacio (aforo 30) no alcanza para un grupo de 40 → infactible con mensaje HC-CAP.
        [Fact]
        public async Task HCCAP_EspacioSinAforoSuficiente_RetornaInfactible()
        {
            var bloques = CrearBloques(4);
            var cohorte = Guid.NewGuid();
            var grupo = new Grupo(cohorte, "G", Guid.NewGuid(), 1, 40); // 40 estudiantes
            var sesion = CrearSesionPresencial(cohorte, TipoAlternancia.SinAlternancia, 1m);
            var labPequeno = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { labPequeno }, Enumerable.Empty<Docente>(),
                grupos: new[] { grupo });

            Assert.False(resultado.EsFactible);
            Assert.Contains("HC-CAP", resultado.MensajeError);
        }

        // Con un espacio de aforo 50 para el mismo grupo de 40 → factible.
        [Fact]
        public async Task HCCAP_EspacioConAforoSuficiente_RetornaFactible()
        {
            var bloques = CrearBloques(4);
            var cohorte = Guid.NewGuid();
            var grupo = new Grupo(cohorte, "G", Guid.NewGuid(), 1, 40);
            var sesion = CrearSesionPresencial(cohorte, TipoAlternancia.SinAlternancia, 1m);
            var labGrande = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 50);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { labGrande }, Enumerable.Empty<Docente>(),
                grupos: new[] { grupo });

            Assert.True(resultado.EsFactible);
        }

        // ── HC-VH: ventana horaria de la asignatura ─────────────────────────────────

        // Con ventana [09:00, 11:00], toda asignación de la sesión cae dentro del rango.
        [Fact]
        public async Task HCVH_VentanaHoraria_AsignaDentroDelRango()
        {
            var bloques = CrearBloques(6); // starts 07:00..12:00
            var asigId = Guid.NewGuid();
            var cohorte = Guid.NewGuid();
            var sesion = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, cohorte,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);
            var ventana = new Dictionary<Guid, (TimeOnly?, TimeOnly?)>
            {
                [asigId] = (new TimeOnly(9, 0), new TimeOnly(11, 0))
            };

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), Enumerable.Empty<Docente>(),
                ventanaPorAsignatura: ventana);

            Assert.True(resultado.EsFactible);
            foreach (var a in resultado.Asignaciones)
            {
                var bl = bloques.First(b => b.Id == a.BloqueTiempoId);
                Assert.True(bl.HoraInicio >= new TimeOnly(9, 0));
                Assert.True(bl.HoraInicio.AddHours(1) <= new TimeOnly(11, 0));
            }
        }

        // Una sesión de 2h no cabe en una ventana de 1h → infactible con mensaje HC-VH.
        [Fact]
        public async Task HCVH_SesionNoCabeEnVentana_RetornaInfactible()
        {
            var bloques = CrearBloques(6);
            var asigId = Guid.NewGuid();
            var cohorte = Guid.NewGuid();
            var sesion = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, cohorte,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);
            var ventana = new Dictionary<Guid, (TimeOnly?, TimeOnly?)>
            {
                [asigId] = (new TimeOnly(9, 0), new TimeOnly(10, 0)) // ventana de 1h
            };

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(), Enumerable.Empty<Docente>(),
                ventanaPorAsignatura: ventana);

            Assert.False(resultado.EsFactible);
            Assert.Contains("HC-VH", resultado.MensajeError);
        }
    }
}
