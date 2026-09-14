using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Engine.Genetic;
using Xunit;

namespace SOEA.Tests.Engine.Genetic
{
    /// <summary>
    /// Operadores del GA bi-semanal (cromosoma = dos inicios por sesión: Start en Semana A,
    /// StartB en Semana B — Incremento 2). Verifica los invariantes: inicios siempre válidos
    /// (cabe-en-día), reparación de solapes de cohorte (HC-C01, CR-08),
    /// StartB == Start para TipoA/TipoB (regla 9 / ALT-05) y divergencia libre de StartB para
    /// SinAlternancia (ALT-06).
    /// </summary>
    public class OperadoresGeneticosTests
    {
        // CR-08: el eje de no-solapamiento es la cohorte (GrupoId); el docente queda fuera.
        private static Sesion CrearSesion(
            Guid grupoId, decimal duracion = 2m, Modalidad modalidad = Modalidad.Presencial,
            TipoAlternancia alternancia = TipoAlternancia.SinAlternancia,
            Guid? asignaturaId = null, Guid? parejaAlternanciaId = null) =>
            new(Guid.NewGuid(), asignaturaId ?? Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                alternancia, modalidad, duracion, false, false, parejaAlternanciaId: parejaAlternanciaId);

        // Lunes y Martes con `bloquesPorDia` bloques de 1h cada uno.
        private static List<BloqueTiempo> CrearGrilla(int bloquesPorDia)
        {
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes })
                for (int h = 0; h < bloquesPorDia; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)));
            return bloques;
        }

        [Fact]
        public void Reparar_ConflictoCohorte_EliminaSolape()
        {
            var cohorteId = Guid.NewGuid();
            var sesiones = new List<Sesion> { CrearSesion(cohorteId, 2m), CrearSesion(cohorteId, 2m) };
            var bloques  = CrearGrilla(10);

            // Ambas en el bloque 0, duración 2 → solapan en los bloques 0 y 1.
            var c = new CromosomaHorario(sesiones.Select(s => s.Id).ToArray(), new[] { 0, 0 });
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(42));
            op.Reparar(c);

            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            Assert.False(BloquesPlanner.Solapan(c.Start[0], 2, c.Start[1], 2, diaPorIdx),
                "La reparación no eliminó el solape de cohorte.");
        }

        [Fact]
        public void Mutar_SiempreProduceInicioQueNoCruzaDia()
        {
            var docenteId = Guid.NewGuid();
            var sesion   = CrearSesion(docenteId, 2m);
            var sesiones = new List<Sesion> { sesion };
            var bloques  = CrearGrilla(5);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var rangos    = BloquesPlanner.RangosPorDia(bloques);

            var c  = new CromosomaHorario(new[] { sesion.Id }, new[] { 0 });
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(42));

            for (int i = 0; i < 200; i++)
            {
                op.Mutar(c, 1.0);
                Assert.True(BloquesPlanner.CabeEnDia(c.Start[0], 2, rangos, diaPorIdx),
                    $"Mutación generó inicio {c.Start[0]} que cruza día (duración 2).");
            }
        }

        // CR-08 / degradación HC-I02: la mutación ya NO se confina a la disponibilidad del docente.
        // El único invariante que conserva es estructural: el inicio cabe-en-día (no cruza el límite
        // del día para la duración dada). El docente está disponible solo Lunes, pero la mutación
        // puede colocar la sesión también en Martes.
        [Fact]
        public void Mutar_RespetaCabeEnDia()
        {
            var docenteId = Guid.NewGuid();
            var sesion   = CrearSesion(docenteId, 2m);
            var sesiones = new List<Sesion> { sesion };
            var bloques  = CrearGrilla(5);             // 0..4 Lunes, 5..9 Martes
            var soloLunes = bloques.Take(5).ToList();
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var rangos    = BloquesPlanner.RangosPorDia(bloques);

            var c  = new CromosomaHorario(new[] { sesion.Id }, new[] { 0 });
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(7));

            for (int i = 0; i < 200; i++)
            {
                op.Mutar(c, 1.0);
                Assert.True(BloquesPlanner.CabeEnDia(c.Start[0], 2, rangos, diaPorIdx),
                    $"Mutación generó inicio {c.Start[0]} que cruza día (duración 2).");
            }
        }

        // Property test: tras muchos cruces+mutaciones+reparaciones, todo inicio sigue válido.
        [Fact]
        public void OperadoresPreservanInvarianteI1()
        {
            var docenteId = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 4).Select(_ => CrearSesion(docenteId, 2m)).ToList();
            var bloques  = CrearGrilla(8);
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(123));

            var ids = sesiones.Select(s => s.Id).ToArray();
            var p1 = new CromosomaHorario(ids,
                sesiones.Select((_, i) => op.StartsValidosDe(i).Count > 0 ? op.StartsValidosDe(i)[0] : 0).ToArray());
            var p2 = new CromosomaHorario(ids, sesiones.Select((_, i) =>
            {
                var v = op.StartsValidosDe(i);
                return v.Count > 0 ? v[v.Count - 1] : 0;
            }).ToArray());

            for (int iter = 0; iter < 500; iter++)
            {
                var hijo = op.Cruce(p1, p2, 0.9);
                op.Mutar(hijo, 0.3);
                op.Reparar(hijo);
                for (int i = 0; i < hijo.CantidadGenes; i++)
                {
                    var validos = op.StartsValidosDe(i);
                    Assert.Contains(hijo.Start[i], validos);
                }
                p1 = hijo;
            }
        }

        // HC-VH: el dominio de los operadores incluye la ventana horaria de la asignatura
        // (misma fuente que CP-SAT: CalculadorDominioSesion). Ninguna mutación puede sacar
        // la sesión de su ventana — el vacío que permitía publicar violaciones de HC-VH.
        [Fact]
        public void Mutar_ConVentanaHoraria_NuncaSaleDeLaVentana()
        {
            var sesion   = CrearSesion(Guid.NewGuid(), 2m);
            var sesiones = new List<Sesion> { sesion };
            var bloques  = CrearGrilla(5); // 07:00..12:00 en Lunes y Martes
            var ventanas = new Dictionary<Guid, (TimeOnly? min, TimeOnly? max)>
            {
                [sesion.AsignaturaId] = (new TimeOnly(8, 0), new TimeOnly(11, 0))
            };

            var c  = new CromosomaHorario(new[] { sesion.Id }, new[] { 1 }); // 08:00, válido
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(42),
                ventanaPorAsignatura: ventanas);

            for (int i = 0; i < 500; i++)
            {
                op.Mutar(c, 1.0);
                var inicio = bloques[c.Start[0]].HoraInicio;
                Assert.True(inicio >= new TimeOnly(8, 0) && inicio.AddHours(2) <= new TimeOnly(11, 0),
                    $"Mutación produjo inicio {inicio} fuera de la ventana [08:00–11:00] (duración 2h).");
            }
        }

        // Si la intersección (día ∩ franja ∩ ventana) queda vacía, el gen se CONGELA en su valor
        // actual (la semilla de Fase 2): antes existía un fallback que abría el dominio completo
        // y podía violar HC-G01/HC-VH.
        [Fact]
        public void DominioVacio_GenSeCongelaEnLaSemilla()
        {
            var sesion   = CrearSesion(Guid.NewGuid(), 2m);
            var sesiones = new List<Sesion> { sesion };
            var bloques  = CrearGrilla(5);
            // Ventana de 1h para una sesión de 2h → dominio vacío.
            var ventanas = new Dictionary<Guid, (TimeOnly? min, TimeOnly? max)>
            {
                [sesion.AsignaturaId] = (new TimeOnly(8, 0), new TimeOnly(9, 0))
            };

            var op = new OperadoresGeneticos(sesiones, bloques, new Random(42),
                ventanaPorAsignatura: ventanas);

            Assert.Empty(op.StartsValidosDe(0));

            var c = new CromosomaHorario(new[] { sesion.Id }, new[] { 3 }); // semilla de Fase 2
            for (int i = 0; i < 100; i++)
            {
                op.Mutar(c, 1.0);
                op.Reparar(c);
                Assert.Equal(3, c.Start[0]); // el gen nunca se mueve
            }
        }

        // ── M4: reparación de HC-ALT y HC-SEP ──────────────────────────────────────────
        // Antes de M4, ni mutación ni cruce respetaban ParejaAlternanciaId ni la separación
        // mínima de días entre sesiones del mismo (grupo, asignatura, tipo) — cualquier run con
        // alternancia dinámica o ≥2 sesiones semanales del mismo tipo violaba HC-ALT/HC-SEP en
        // casi cualquier generación, y el post-chequeo del orquestador forzaba fallback a Fase 2.

        // M4: dos sesiones con el mismo ParejaAlternanciaId deben terminar en el MISMO start tras
        // Reparar, aunque se siembren desincronizadas — _startsValidos no codifica el
        // emparejamiento, así que mutación/cruce las puede separar libremente.
        [Fact]
        public void Reparar_ParejaAlternancia_SincronizaAmbosMiembrosAlMismoStart()
        {
            var grupo1 = Guid.NewGuid();
            var grupo2 = Guid.NewGuid();
            var patron = Guid.NewGuid();
            var s1 = CrearSesion(grupo1, 1m, alternancia: TipoAlternancia.TipoA, parejaAlternanciaId: patron);
            var s2 = CrearSesion(grupo2, 1m, alternancia: TipoAlternancia.TipoB, parejaAlternanciaId: patron);
            var sesiones = new List<Sesion> { s1, s2 };
            var bloques = CrearGrilla(6);
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(3));

            // Sembrados deliberadamente desincronizados.
            var c = new CromosomaHorario(sesiones.Select(s => s.Id).ToArray(), new[] { 0, 4 });
            op.Reparar(c);

            Assert.Equal(c.Start[0], c.Start[1]);
        }

        // Property test: tras muchos ciclos de cruce+mutación+reparación, una pareja de
        // alternancia nunca pierde la sincronía de bloque. Con exactamente 2 genes, Cruce solo
        // tiene un punto de corte posible (índice 1) → cada hijo hereda el gen 0 de un padre y el
        // gen 1 del otro, garantizando desincronía máxima antes de cada Reparar.
        [Fact]
        public void CicloCompleto_ParejaAlternancia_MantieneSincroniaDeBloque()
        {
            var grupo1 = Guid.NewGuid();
            var grupo2 = Guid.NewGuid();
            var patron = Guid.NewGuid();
            var s1 = CrearSesion(grupo1, 1m, alternancia: TipoAlternancia.TipoA, parejaAlternanciaId: patron);
            var s2 = CrearSesion(grupo2, 1m, alternancia: TipoAlternancia.TipoB, parejaAlternanciaId: patron);
            var sesiones = new List<Sesion> { s1, s2 };
            var bloques = CrearGrilla(8);
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(11));

            var ids = sesiones.Select(s => s.Id).ToArray();
            var p1 = new CromosomaHorario(ids, new[] { 0, 0 });
            var p2 = new CromosomaHorario(ids, new[] { 3, 3 });

            for (int iter = 0; iter < 200; iter++)
            {
                var hijo = op.Cruce(p1, p2, 0.9);
                op.Mutar(hijo, 0.5);
                op.Reparar(hijo);
                Assert.Equal(hijo.Start[0], hijo.Start[1]);
                p1 = hijo;
            }
        }

        // M4: dos sesiones del mismo (grupo, asignatura, tipo) sembradas sin separación mínima de
        // días deben terminar separadas ≥2 días tras Reparar (misma regla que CP-SAT/validador:
        // ReglasSesion.SeparacionDiasOk).
        [Fact]
        public void Reparar_SeparacionDeDiasInsuficiente_ReubicaHastaSepararlas()
        {
            var grupo = Guid.NewGuid();
            var asig  = Guid.NewGuid();
            var s1 = CrearSesion(grupo, 1m, asignaturaId: asig);
            var s2 = CrearSesion(grupo, 1m, asignaturaId: asig);
            var sesiones = new List<Sesion> { s1, s2 };

            // 5 días (Lunes..Viernes) × 4 bloques: dominio suficiente para separar sin cruzar día.
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in new[]
                     { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles, DiaDeSemana.Jueves, DiaDeSemana.Viernes })
                for (int h = 0; h < 4; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)));
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var op = new OperadoresGeneticos(sesiones, bloques, new Random(5));

            // Ambas sembradas el mismo día (Lunes, bloques 0 y 1): viola HC-SEP.
            var c = new CromosomaHorario(sesiones.Select(s => s.Id).ToArray(), new[] { 0, 1 });
            op.Reparar(c);

            Assert.True(ReglasSesion.SeparacionDiasOk(diaPorIdx[c.Start[0]], diaPorIdx[c.Start[1]]),
                $"HC-SEP: sesiones siguen sin separación mínima ({diaPorIdx[c.Start[0]]} / {diaPorIdx[c.Start[1]]}).");
        }
    }
}
