using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Engine.Genetic;
using Xunit;

namespace SOEA.Tests.Engine.Genetic
{
    /// <summary>
    /// Verifica los cuatro objetivos blandos reales del GA: ① huecos, ② &gt; 6 horas seguidas,
    /// ③ balance entre los días operativos de la grilla (desviación media absoluta), ④ balance de
    /// carga entre Semana A y B (SC-BAL, Incremento 2). CR-08: la ergonomía se mide por cohorte
    /// (GrupoId), no por docente. Cada test aísla un objetivo vía los pesos, usando sesiones
    /// virtuales para neutralizar la guarda de aulas.
    /// </summary>
    public class EvaluadorFitnessTests
    {
        private static List<BloqueTiempo> Grilla(int n, params DiaDeSemana[] dias)
        {
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in (dias.Length == 0 ? new[] { DiaDeSemana.Lunes } : dias))
                for (int h = 0; h < n; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)));
            return bloques;
        }

        // CR-08: la ergonomía se mide por cohorte (GrupoId); el docente queda fuera.
        private static Sesion Virtual(Guid grupoId, decimal dur) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, dur, false, false);

        private static Docente Doc(Guid id, IEnumerable<BloqueTiempo> bloques)
        {
            var d = new Docente(id, "Doc", "", $"doc-{id}@soea.edu", 40m,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
            foreach (var b in bloques) d.AgregarBloqueDisponibilidad(b);
            return d;
        }

        [Fact] // ① SC-01: un horario con hueco penaliza más que uno compacto.
        public void SC01_PenalizaHuecos()
        {
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Virtual(docId, 1m), Virtual(docId, 1m) };
            var bloques  = Grilla(5);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var cfg = new ConfiguracionOptimizacion(PesoErgo: 1, PesoTiempos: 0, PesoAlmuerzo: 0);
            var eval = new EvaluadorFitness(sesiones, bloques, docentes, new List<Espacio>(), cfg);

            var ids = sesiones.Select(s => s.Id).ToArray();
            var compacto = new CromosomaHorario(ids, new[] { 0, 1 }); // contiguas
            var conHueco = new CromosomaHorario(ids, new[] { 0, 3 }); // hueco de 2h

            Assert.True(eval.Evaluar(conHueco) > eval.Evaluar(compacto));
        }

        [Fact] // ② SC-09: una racha de 7h penaliza; cortarla a 6h + 1h no.
        public void SC09_PenalizaMasDeSeisHorasSeguidas()
        {
            var docId = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 7).Select(_ => Virtual(docId, 1m)).ToList();
            var bloques  = Grilla(8);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var cfg = new ConfiguracionOptimizacion(PesoErgo: 0, PesoTiempos: 0, PesoAlmuerzo: 1);
            var eval = new EvaluadorFitness(sesiones, bloques, docentes, new List<Espacio>(), cfg);

            var ids = sesiones.Select(s => s.Id).ToArray();
            var contiguo = new CromosomaHorario(ids, new[] { 0, 1, 2, 3, 4, 5, 6 }); // racha de 7h
            var conCorte = new CromosomaHorario(ids, new[] { 0, 1, 2, 3, 4, 5, 7 }); // 6h + corte + 1h

            Assert.True(eval.Evaluar(contiguo) > eval.Evaluar(conCorte));
        }

        [Fact] // ③ SC-06: concentrar en un día penaliza más que repartir entre los días de la grilla.
        public void SC06_BalanceaEntreDiasDeGrilla()
        {
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Virtual(docId, 1m), Virtual(docId, 1m) };
            var bloques  = Grilla(4, DiaDeSemana.Lunes, DiaDeSemana.Martes); // Lunes 0-3, Martes 4-7
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var cfg = new ConfiguracionOptimizacion(PesoErgo: 0, PesoTiempos: 1, PesoAlmuerzo: 0);
            var eval = new EvaluadorFitness(sesiones, bloques, docentes, new List<Espacio>(), cfg);

            var ids = sesiones.Select(s => s.Id).ToArray();
            var concentrado = new CromosomaHorario(ids, new[] { 0, 1 }); // ambas Lunes
            var balanceado  = new CromosomaHorario(ids, new[] { 0, 4 }); // una Lunes, una Martes

            Assert.True(eval.Evaluar(concentrado) > eval.Evaluar(balanceado));
        }

        [Fact] // ④ SC-BAL: desbalance de carga entre Semana A y B penaliza más que carga simétrica.
        public void SCBAL_PenalizaDesbalanceEntreSemanas()
        {
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Virtual(docId, 2m) }; // SinAlternancia: StartB libre
            var bloques  = Grilla(4, DiaDeSemana.Lunes, DiaDeSemana.Martes); // Lunes 0-3, Martes 4-7
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var cfg = new ConfiguracionOptimizacion(
                PesoErgo: 0, PesoTiempos: 0, PesoAlmuerzo: 0, PesoBalanceSemanas: 1);
            var eval = new EvaluadorFitness(sesiones, bloques, docentes, new List<Espacio>(), cfg);

            var ids = sesiones.Select(s => s.Id).ToArray();
            var balanceado    = new CromosomaHorario(ids, new[] { 0 }, new[] { 0 }); // misma franja A y B
            var desbalanceado = new CromosomaHorario(ids, new[] { 0 }, new[] { 4 }); // Lunes en A, Martes en B

            Assert.True(eval.Evaluar(desbalanceado) > eval.Evaluar(balanceado));
        }

        // ⑤ SC-PRES: ceder presencialidad (Alternancia != SinAlternancia) de una sesión única +
        // Obligatoria penaliza MÁS que la de una 2ª sesión + Electiva. El término es constante por
        // cromosoma (no lo mueve el GA), así que comparamos dos evaluadores con info distinta.
        [Fact]
        public void SCPRES_PenalizaProporcionalALaPrioridad()
        {
            var grupo = Guid.NewGuid();
            var asig = Guid.NewGuid();
            // Sesión que cedió presencialidad (TipoA). Modalidad.Virtual neutraliza la guarda de aulas.
            var sesion = new Sesion(Guid.NewGuid(), asig, null, Guid.NewGuid(), null, grupo,
                TipoAlternancia.TipoA, Modalidad.Virtual, 1m, false, false);
            var bloques = Grilla(4);
            var docentes = new List<Docente>();
            var cfg = new ConfiguracionOptimizacion(
                PesoErgo: 0, PesoTiempos: 0, PesoAlmuerzo: 0, PesoBalanceSemanas: 0, PesoPresencialFirst: 1);

            var infoAlta = new Dictionary<Guid, (int, CategoriaAsignatura)>
                { [asig] = (1, CategoriaAsignatura.Obligatoria) }; // única + Obligatoria → peor
            var infoBaja = new Dictionary<Guid, (int, CategoriaAsignatura)>
                { [asig] = (2, CategoriaAsignatura.Electiva) };    // 2/sem + Electiva → menor

            var evalAlta = new EvaluadorFitness(new List<Sesion> { sesion }, bloques, docentes, new List<Espacio>(), cfg, infoAlta);
            var evalBaja = new EvaluadorFitness(new List<Sesion> { sesion }, bloques, docentes, new List<Espacio>(), cfg, infoBaja);

            var ids = new[] { sesion.Id };
            var crom = new CromosomaHorario(ids, new[] { 0 }, new[] { 0 });

            Assert.True(evalAlta.Evaluar(crom) > evalBaja.Evaluar(crom));
        }
    }
}
