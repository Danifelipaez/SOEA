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

        // Un bloque de 1h por día, lunes..viernes (para HC-SEP: necesita más de un día en la grilla).
        private static List<BloqueTiempo> CrearBloquesMultiDia(int dias = 5) =>
            new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles, DiaDeSemana.Jueves, DiaDeSemana.Viernes }
                .Take(dias)
                .Select(dia => new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7, 0), new TimeOnly(8, 0)))
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
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>());

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
                sesiones, bloques, Enumerable.Empty<Espacio>());

            Assert.False(resultado.EsFactible);
            // M3 (auditoría): esto es HC-C01 puro (0 espacios en el run) — antes del fix caía en el
            // catch-all genérico del solver, que SIEMPRE reportaba Espacio sin importar la causa real.
            Assert.Equal(MotivoInfactibilidad.Otro, resultado.Motivo);
        }

        // T2 (auditoria de suavizado): este test se llamaba
        // "Docente_ConBloquesSuficientes_SesionVirtual_RetornaFactible" y su comentario
        // reclamaba cobertura de HC-I02+HC-I03, pero ResolverFactibilidadAsync ya no recibe
        // docentes (CR-08/CR-02: la disponibilidad y la carga del docente salieron del
        // pipeline, ver AsignarDocenteSesionService) — el Docente que construia el test nunca
        // llegaba al solver. Lo que de verdad verifica: una sesion virtual con bloques
        // suficientes en la grilla resuelve factible.
        [Fact]
        public async Task SesionVirtualConBloquesSuficientes_RetornaFactible()
        {
            var bloques = CrearBloques(3);
            var sesion = CrearSesion(Guid.NewGuid(), 2m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>());

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
                sesiones, bloques, Enumerable.Empty<Espacio>());

            Assert.False(resultado.EsFactible);
            // M3: mismo caso — HC-C01 puro, sin espacios en el run, ya no se etiqueta Espacio.
            Assert.Equal(MotivoInfactibilidad.Otro, resultado.Motivo);
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
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>());

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Otro, resultado.Motivo);
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
                new[] { a, b }, bloques, Enumerable.Empty<Espacio>());

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
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>());

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
                new[] { sesion }, bloques, new[] { lab });

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
                new[] { sesionA, sesionB }, bloques, new[] { lab });

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
                new[] { sesion }, bloques, new[] { CrearLaboratorio() });

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
            var grupo = new Grupo(cohorte, "G", Guid.NewGuid(), 40); // 40 estudiantes
            var sesion = CrearSesionPresencial(cohorte, TipoAlternancia.SinAlternancia, 1m);
            var labPequeno = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { labPequeno },
                grupos: new[] { grupo });

            Assert.False(resultado.EsFactible);
            Assert.Contains("Capacidad insuficiente", resultado.MensajeError);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
        }

        // Con un espacio de aforo 50 para el mismo grupo de 40 → factible.
        [Fact]
        public async Task HCCAP_EspacioConAforoSuficiente_RetornaFactible()
        {
            var bloques = CrearBloques(4);
            var cohorte = Guid.NewGuid();
            var grupo = new Grupo(cohorte, "G", Guid.NewGuid(), 40);
            var sesion = CrearSesionPresencial(cohorte, TipoAlternancia.SinAlternancia, 1m);
            var labGrande = new Espacio(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 50);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { labGrande },
                grupos: new[] { grupo });

            Assert.True(resultado.EsFactible);
        }

        // ── M1 (auditoría): el pre-check de capacidad debe particionar por clase de espacio, no
        // sumar todo el inventario en una sola bolsa — el bug reportado ("todo cae en Salon 201")
        // es justo lo simétrico de esto: un run con salones de sobra y 0 laboratorios debía fallar
        // por falta de LABORATORIOS, no colarse porque los salones inflan la capacidad total.

        [Fact]
        public async Task M1_DemandaDeLaboratorioConSalonesDeSobra_InfactibleNombrandoLaboratorios()
        {
            // Pooled (bug anterior): 3 salones × 4 bloques = 12h de capacidad total vs 2h de demanda
            // total → "factible" por error. Particionado (fix): 0h de capacidad de laboratorio vs 2h
            // de demanda de laboratorio → infactible, y el mensaje nombra la clase que falta.
            var bloques = CrearBloques(4);
            var salones = Enumerable.Range(0, 3)
                .Select(i => new Espacio(Guid.NewGuid(), $"Salón {i}", TipoEspacio.Salon, 30))
                .ToList();
            var sesionLab = CrearSesionPresencial(Guid.NewGuid(), TipoAlternancia.SinAlternancia, 2m); // tipoFlujo default = Laboratorio

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesionLab }, bloques, salones);

            Assert.False(resultado.EsFactible);
            Assert.Contains("laboratorios", resultado.MensajeError);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
        }

        [Fact]
        public async Task M1_DemandaDeTeoriaConLaboratoriosDeSobra_InfactibleNombrandoSalonesAuditorios()
        {
            var bloques = CrearBloques(4);
            var labs = Enumerable.Range(0, 3).Select(_ => CrearLaboratorio()).ToList();
            var sesionTeoria = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesionTeoria }, bloques, labs);

            Assert.False(resultado.EsFactible);
            Assert.Contains("salones/auditorios", resultado.MensajeError);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
        }

        // ── HC-G01: disponibilidad declarada por grupo (P1 — antes no había ni un test que le
        // pasara a CP-SAT un grupo con disponibilidad real; la disponibilidad nunca llegaba al
        // motor). Dos grupos con disponibilidad distinta deben producir dominios de inicio
        // distintos para sus sesiones.

        [Fact]
        public async Task HCG01_DosGruposConDisponibilidadDistinta_AsignanEnFranjasDistintas()
        {
            var bloques = CrearBloques(10); // starts 07:00..16:00 → cruza mediodía
            var cohorteMatutina = Guid.NewGuid();
            var cohorteVespertina = Guid.NewGuid();

            // Franja específica (no la etiqueta "Matutino"/"Vespertino"): esa ventana fija histórica
            // llega hasta las 13:00 y por tanto toca la hora 12 (vespertino en PerteneceAFranja) —
            // aquí se necesita un corte limpio en el mediodía para que el test sea inequívoco.
            var grupoMatutino = new Grupo(cohorteMatutina, "Matutino", Guid.NewGuid(), 20);
            grupoMatutino.ActualizarDisponibilidadUi(
                """{"lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"06:00","hasta":"11:00"}}""");

            var grupoVespertino = new Grupo(cohorteVespertina, "Vespertino", Guid.NewGuid(), 20);
            grupoVespertino.ActualizarDisponibilidadUi(
                """{"lunes":{"noDisponible":false,"tipo":"Franja específica","desde":"13:00","hasta":"18:00"}}""");

            var sesionMatutina = CrearSesion(cohorteMatutina, duracion: 1m);
            var sesionVespertina = CrearSesion(cohorteVespertina, duracion: 1m);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesionMatutina, sesionVespertina }, bloques, Enumerable.Empty<Espacio>(),
                grupos: new[] { grupoMatutino, grupoVespertino });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            var bloqueMatutino = bloques.First(b => b.Id ==
                resultado.Asignaciones.First(a => a.SesionId == sesionMatutina.Id).BloqueTiempoId);
            var bloqueVespertino = bloques.First(b => b.Id ==
                resultado.Asignaciones.First(a => a.SesionId == sesionVespertina.Id).BloqueTiempoId);

            Assert.True(bloqueMatutino.HoraInicio.Hour < 12);
            Assert.True(bloqueVespertino.HoraInicio.Hour >= 12);
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
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(),
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
                new[] { sesion }, bloques, Enumerable.Empty<Espacio>(),
                ventanaPorAsignatura: ventana);

            Assert.False(resultado.EsFactible);
            Assert.Contains("Fuera de la ventana horaria", resultado.MensajeError);
            Assert.Equal(MotivoInfactibilidad.VentanaHoraria, resultado.Motivo);
        }

        // ── HC-SEP: separación mínima de días entre sesiones semanales del mismo
        // (grupo, asignatura, tipo de sesión) — petición 11 (P2.3).

        [Fact]
        public async Task HCSEP_DosSesionesSemanalesMismaAsignaturaYTipo_QuedanConSeparacionDeDias()
        {
            var bloques = CrearBloquesMultiDia(5); // lunes..viernes, 1 bloque/día
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var s1 = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);
            var s2 = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { s1, s2 }, bloques, Enumerable.Empty<Espacio>());

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            var b1 = bloques.First(b => b.Id == resultado.Asignaciones.First(a => a.SesionId == s1.Id).BloqueTiempoId);
            var b2 = bloques.First(b => b.Id == resultado.Asignaciones.First(a => a.SesionId == s2.Id).BloqueTiempoId);
            Assert.True(Math.Abs((int)b1.Dia - (int)b2.Dia) >= 2, $"Días sin separación mínima: {b1.Dia} / {b2.Dia}");
        }

        // M5 (auditoría): con separación mínima de 2 días en un rango de 6 (lunes..sábado), el
        // mayor conjunto pairwise-separado posible es 3 (lunes/miércoles/viernes) — 3 sesiones
        // semanales del mismo (grupo, asignatura, tipo) siguen siendo factibles.
        [Fact]
        public async Task HCSEP_TresSesionesSemanalesMismaAsignaturaYTipo_SonFactibles()
        {
            var bloques = CrearBloquesMultiDia(5);
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 3).Select(_ =>
                new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual)
            ).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>());

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }

        // M5 (auditoría): un cluster de 4+ sesiones semanales del mismo (grupo, asignatura, tipo)
        // es estructuralmente infactible por HC-SEP — antes caía en el catch-all genérico del
        // solver, que el fix de M3 dejó de etiquetar como Espacio; ahora se detecta ANTES de
        // construir el modelo, con mensaje propio, y sin disparar el bucle de cesión de labs.
        [Fact]
        public async Task HCSEP_ClusterDeCuatroSesiones_RetornaInfactibleConMensajeDedicado()
        {
            var bloques = CrearBloquesMultiDia(5);
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 4).Select(_ =>
                new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual)
            ).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>());

            Assert.False(resultado.EsFactible);
            Assert.Contains("separación mínima de 2 días", resultado.MensajeError);
            Assert.Equal(MotivoInfactibilidad.Otro, resultado.Motivo);
        }

        [Fact]
        public async Task HCSEP_ClusterDeCuatroSesiones_SesionesFijasNoCuentanParaElCluster()
        {
            // Las sesiones fijas del horario base quedan fuera de HC-SEP (mismo criterio que el
            // dominio normal — ya están fijadas por igualdad). 3 libres + 1 fija = no dispara el
            // pre-check, aunque haya 4 sesiones en total del mismo (grupo, asignatura, tipo).
            var bloques = CrearBloquesMultiDia(5);
            var grupoId = Guid.NewGuid();
            var asigId = Guid.NewGuid();
            var libres = Enumerable.Range(0, 3).Select(_ =>
                new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual)
            ).ToList();
            var fija = new Sesion(Guid.NewGuid(), asigId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);
            var sesiones = libres.Append(fija).ToList();

            var resultado = await Motor.ResolverFactibilidadAsync(
                sesiones, bloques, Enumerable.Empty<Espacio>(),
                sesionesFijasIds: new[] { fija.Id });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
        }

        // ── HC-S03: tipo de espacio según TipoSesion (A2/A3) — petición 7 (P2.2). Antes solo se
        // protegían los laboratorios; una teoría presencial podía caer en cualquier espacio,
        // incluido un laboratorio.

        [Fact]
        public async Task HCS03_TeoriaPresencialSinRequisito_NuncaCaeEnLaboratorio()
        {
            var bloques = CrearBloques(2);
            var grupoId = Guid.NewGuid();
            var lab = CrearLaboratorio();
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { lab, salon });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            // SinAlternancia: presencial en ambas semanas — deben coincidir en el mismo salón.
            Assert.All(resultado.Asignaciones.Where(a => a.SesionId == sesion.Id),
                a => Assert.Equal(salon.Id, a.EspacioId));
        }

        [Fact]
        public async Task HCS03_TeoriaPresencialSinRequisito_SoloHayLaboratorio_RetornaInfactible()
        {
            var bloques = CrearBloques(2);
            var grupoId = Guid.NewGuid();
            var lab = CrearLaboratorio();
            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { lab });

            Assert.False(resultado.EsFactible);
        }

        // ── HC-S01 puro: dos presenciales de la MISMA semana no comparten espacio (sin el caso
        // ALT, que sí lo permite porque cae en semanas distintas) — M9, cero cobertura previa.

        // Teoría presencial (no CrearSesionPresencial: su default es TipoFlujo.Laboratorio, que
        // exigiría espacios de tipo Laboratorio y no probaría lo que HC-S01 protege aquí).
        private static Sesion CrearSesionTeoriaPresencial(Guid grupoId, decimal duracion = 1m) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracion, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);

        [Fact]
        public async Task HCS01_DosPresencialesMismaSemana_MismoBloque_NoComparteElUnicoEspacio()
        {
            var bloques = CrearBloques(1); // un único bloque: ambas sesiones deben caer ahí
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var s1 = CrearSesionTeoriaPresencial(Guid.NewGuid());
            var s2 = CrearSesionTeoriaPresencial(Guid.NewGuid());

            // Un solo espacio, un solo bloque, dos sesiones presenciales de grupos distintos
            // (sin alternancia, así que ambas son presenciales en A y en B): HC-S01 debe impedir
            // que compartan el espacio en la misma semana → infactible.
            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { s1, s2 }, bloques, new[] { salon });

            Assert.False(resultado.EsFactible);
        }

        [Fact]
        public async Task HCS01_DosPresencialesMismaSemana_ConDosEspacios_SeReparten()
        {
            var bloques = CrearBloques(1);
            var salonA = new Espacio(Guid.NewGuid(), "Salón A", TipoEspacio.Salon, 30);
            var salonB = new Espacio(Guid.NewGuid(), "Salón B", TipoEspacio.Salon, 30);
            var s1 = CrearSesionTeoriaPresencial(Guid.NewGuid());
            var s2 = CrearSesionTeoriaPresencial(Guid.NewGuid());

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { s1, s2 }, bloques, new[] { salonA, salonB });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            var espacioS1A = resultado.Asignaciones.Single(a => a.SesionId == s1.Id && a.Semana == SemanaAcademica.A).EspacioId;
            var espacioS2A = resultado.Asignaciones.Single(a => a.SesionId == s2.Id && a.Semana == SemanaAcademica.A).EspacioId;
            Assert.NotEqual(espacioS1A, espacioS2A);
        }

        // ── HC-S05 (espacio fijo declarado en Grupo.RequisitosEspacio) en CP-SAT — M9: antes
        // sólo se probaba el equivalente en el GA (AsignadorEspacios) y en el validador; el
        // camino directo por CP-SAT (MotorConstraintProgramming.cs:363-386) no tenía ningún test.

        [Fact]
        public async Task HCS05_RequisitoDeGrupoConEspacioFijo_SeRespetaEnCPSAT()
        {
            var bloques = CrearBloques(2);
            var grupoId = Guid.NewGuid();
            var fijo = new Espacio(Guid.NewGuid(), "Fijo", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var grupo = new Grupo(grupoId, "G", Guid.NewGuid(), 20);
            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio.Salon, 1)
            });
            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { fijo, otro },
                grupos: new[] { grupo });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            Assert.All(resultado.Asignaciones.Where(a => a.SesionId == sesion.Id),
                a => Assert.Equal(fijo.Id, a.EspacioId));
        }

        [Fact]
        public async Task HCS05_RequisitoDeGrupoConEspacioFijo_SinEseEspacioEnElRun_RetornaInfactible()
        {
            // A diferencia de Sesion.EspacioId ausente del run (que sí cae al filtro genérico por
            // tipo — M8 del análisis, CalculadorEspaciosSesion.cs:61-69), un EspacioId fijo que
            // viene de Grupo.RequisitosEspacio NO tiene ese fall-through: CumpleTipo (:36) evalúa
            // "requisito?.EspacioId is Guid fijo" incondicionalmente en ambos pases de Candidatos,
            // así que ningún espacio del run "cumple" y la sesión queda sin candidatos. Asimetría
            // real entre las dos fuentes de espacio fijo, documentada aquí (no corregida — Fase 2).
            var bloques = CrearBloques(2);
            var grupoId = Guid.NewGuid();
            var fijo = new Espacio(Guid.NewGuid(), "Fijo (no está en el run)", TipoEspacio.Salon, 30);
            var otro = new Espacio(Guid.NewGuid(), "Otro", TipoEspacio.Salon, 30);
            var grupo = new Grupo(grupoId, "G", Guid.NewGuid(), 20);
            grupo.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.TeoriaPresencial, fijo.Id, TipoEspacio.Salon, 1)
            });
            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false, tipoFlujo: TipoFlujo.AulaVirtual);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { sesion }, bloques, new[] { otro },
                grupos: new[] { grupo });

            Assert.False(resultado.EsFactible);
            Assert.Equal(MotivoInfactibilidad.Espacio, resultado.Motivo);
        }

        // ── HC-ALT: alternancia atómica por espacio (A4/VERIFICA) — petición del bloque VERIFICA
        // (P2.4). Una pareja (mismo ParejaAlternanciaId, tipos opuestos) debe compartir bloque y
        // espacio: una presencial en semana A, la otra en semana B, en el mismo salón.

        [Fact]
        public async Task HCALT_ParejaDeAlternancia_ComparteBloqueYEspacio()
        {
            var bloques = CrearBloques(3);
            var salon = new Espacio(Guid.NewGuid(), "Salón", TipoEspacio.Salon, 30);
            var patron = Guid.NewGuid();

            var s1 = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.TipoA, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual, patronAlternanciaId: TipoAlternanciaConfig.IdTipoA,
                parejaAlternanciaId: patron);
            var s2 = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.TipoB, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual, patronAlternanciaId: TipoAlternanciaConfig.IdTipoB,
                parejaAlternanciaId: patron);

            var resultado = await Motor.ResolverFactibilidadAsync(
                new[] { s1, s2 }, bloques, new[] { salon });

            Assert.True(resultado.EsFactible, resultado.MensajeError);

            var a1A = resultado.Asignaciones.Single(a => a.SesionId == s1.Id && a.Semana == SemanaAcademica.A);
            var a2A = resultado.Asignaciones.Single(a => a.SesionId == s2.Id && a.Semana == SemanaAcademica.A);
            Assert.Equal(a1A.BloqueTiempoId, a2A.BloqueTiempoId); // mismo horario

            var a2B = resultado.Asignaciones.Single(a => a.SesionId == s2.Id && a.Semana == SemanaAcademica.B);
            Assert.Equal(salon.Id, a1A.EspacioId);   // s1 presencial en semana A
            Assert.Equal(salon.Id, a2B.EspacioId);   // s2 presencial en semana B
            Assert.Equal(a1A.EspacioId, a2B.EspacioId); // mismo espacio entre semanas presenciales
        }
    }
}
