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
    /// Verifica los tres objetivos blandos reales del GA: ① huecos, ② &gt; 6 horas seguidas,
    /// ③ balance entre días disponibles (desviación media absoluta). Cada test aísla un objetivo
    /// vía los pesos, usando sesiones virtuales para neutralizar la guarda de aulas.
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

        private static Sesion Virtual(Guid docenteId, decimal dur) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null,
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

        [Fact] // ③ SC-06: concentrar en un día penaliza más que repartir entre los días disponibles.
        public void SC06_BalanceaEntreDiasDisponibles()
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
    }
}
