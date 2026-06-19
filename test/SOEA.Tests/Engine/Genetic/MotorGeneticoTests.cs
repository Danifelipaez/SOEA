using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Engine.Genetic;
using Xunit;

namespace SOEA.Tests.Engine.Genetic
{
    /// <summary>
    /// Pruebas de orquestación del GA bi-semanal: produce asignaciones A/B válidas, preserva
    /// la alternancia (regla 9) para TipoA/TipoB y permite franjas independientes para
    /// SinAlternancia (ALT-06, Incremento 2), respeta el espacio solo en semanas presenciales,
    /// es determinista con semilla fija y hace fallback a Fase 2 cuando no hay solución de aulas
    /// factible.
    /// </summary>
    public class MotorGeneticoTests
    {
        private static readonly MotorGenetico Motor = new(NullLogger<MotorGenetico>.Instance);

        private static List<BloqueTiempo> Grilla(int n, params DiaDeSemana[] dias)
        {
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in (dias.Length == 0 ? new[] { DiaDeSemana.Lunes } : dias))
                for (int h = 0; h < n; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)));
            return bloques;
        }

        private static Sesion Sesion(Guid docenteId, TipoAlternancia alt, Modalidad modalidad, decimal dur) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null, alt, modalidad, dur, false, false);

        private static Docente Doc(Guid id, IEnumerable<BloqueTiempo> bloques)
        {
            var d = new Docente(id, "Doc", "", $"doc-{id}@soea.edu", 40m,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
            foreach (var b in bloques) d.AgregarBloqueDisponibilidad(b);
            return d;
        }

        // Construye una solución de Fase 2 (dos asignaciones por sesión) con inicios dados.
        private static List<AsignacionSemanal> Fase2(
            List<Sesion> sesiones, int[] inicio, List<BloqueTiempo> bloques, List<Espacio> espacios)
        {
            var lista = new List<AsignacionSemanal>();
            for (int i = 0; i < sesiones.Count; i++)
                foreach (var w in new[] { SemanaAcademica.A, SemanaAcademica.B })
                {
                    var modalidad = ModalidadSemanal.Derivar(sesiones[i], w);
                    Guid? esp = modalidad == Modalidad.Presencial && espacios.Count > 0 ? espacios[0].Id : null;
                    lista.Add(new AsignacionSemanal(Guid.NewGuid(), sesiones[i].Id, w, bloques[inicio[i]].Id, esp, modalidad));
                }
            return lista;
        }

        private static ConfiguracionOptimizacion Cfg(int? semilla = 42) =>
            new(TamañoPoblacion: 10, MaxGeneraciones: 30, Semilla: semilla);

        [Fact]
        public async Task DosVirtuales_ProduceCuatroAsignaciones_SinFallback()
        {
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m),
                                              Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m) };
            var bloques  = Grilla(8);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio>();
            var fase2 = Fase2(sesiones, new[] { 0, 2 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg());

            Assert.False(r.UsoFallback);
            Assert.Equal(4, r.AsignacionesOptimizadas.Count);
            Assert.All(r.AsignacionesOptimizadas, a =>
            {
                Assert.Equal(Modalidad.Virtual, a.Modalidad);
                Assert.Null(a.EspacioId);
            });
        }

        [Fact]
        public async Task TipoA_Presencial_TieneAulaSoloEnSemanaA()
        {
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Sesion(docId, TipoAlternancia.TipoA, Modalidad.Presencial, 1m) };
            var bloques  = Grilla(6);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30) };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg());

            Assert.False(r.UsoFallback);
            var a = r.AsignacionesOptimizadas.Single(x => x.Semana == SemanaAcademica.A);
            var b = r.AsignacionesOptimizadas.Single(x => x.Semana == SemanaAcademica.B);
            Assert.Equal(Modalidad.Presencial, a.Modalidad);
            Assert.NotNull(a.EspacioId);
            Assert.Equal(Modalidad.Virtual, b.Modalidad);
            Assert.Null(b.EspacioId);
            // Regla 9: misma franja en ambas semanas.
            Assert.Equal(a.BloqueTiempoId, b.BloqueTiempoId);
        }

        [Fact]
        public async Task SinAlternancia_PuedeTenerFranjaDistintaEntreSemanaAYB()
        {
            // Contraste con TipoA_Presencial_TieneAulaSoloEnSemanaA: para SinAlternancia (ALT-06)
            // el Incremento 2 permite que la franja de Semana B difiera de la de Semana A.
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m) };
            var bloques  = Grilla(6); // un solo día (Lunes), bloques 0..5
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Lab", TipoEspacio.Laboratorio, 30) };

            // Fase 2 ya trae franjas distintas entre semanas para esta sesión (a diferencia del
            // helper Fase2(), que siempre las siembra iguales).
            var fase2 = new List<AsignacionSemanal>
            {
                new(Guid.NewGuid(), sesiones[0].Id, SemanaAcademica.A, bloques[0].Id, espacios[0].Id, Modalidad.Presencial),
                new(Guid.NewGuid(), sesiones[0].Id, SemanaAcademica.B, bloques[3].Id, espacios[0].Id, Modalidad.Presencial),
            };

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg());

            Assert.False(r.UsoFallback);
            var a = r.AsignacionesOptimizadas.Single(x => x.Semana == SemanaAcademica.A);
            var b = r.AsignacionesOptimizadas.Single(x => x.Semana == SemanaAcademica.B);
            Assert.NotEqual(a.BloqueTiempoId, b.BloqueTiempoId);
        }

        [Fact]
        public async Task ConSemillaFija_EsDeterminista()
        {
            var docId = Guid.NewGuid();
            var sesiones = Enumerable.Range(0, 3)
                .Select(_ => Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m)).ToList();
            var bloques  = Grilla(8);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio>();
            var fase2 = Fase2(sesiones, new[] { 0, 1, 2 }, bloques, espacios);

            var r1 = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg(99));
            var r2 = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg(99));

            Assert.Equal(r1.PuntajeFitness, r2.PuntajeFitness);
            Assert.Equal(
                r1.AsignacionesOptimizadas.Select(a => a.BloqueTiempoId),
                r2.AsignacionesOptimizadas.Select(a => a.BloqueTiempoId));
        }

        [Fact]
        public async Task SinAulasSuficientes_HaceFallbackAFase2()
        {
            // Un solo bloque disponible y dos sesiones presenciales (distinto docente, sin conflicto
            // de docente) que deben caer en él: requieren 2 aulas pero solo hay 1 → fallback.
            var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid();
            var bloques = Grilla(1); // un único bloque, Lunes
            var sesiones = new List<Sesion>
            {
                Sesion(d1, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m),
                Sesion(d2, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m)
            };
            var docentes = new List<Docente> { Doc(d1, bloques), Doc(d2, bloques) };
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Aula", TipoEspacio.Salon, 30) };
            var fase2 = Fase2(sesiones, new[] { 0, 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, Cfg());

            Assert.True(r.UsoFallback);
            Assert.Equal(fase2.Count, r.AsignacionesOptimizadas.Count);
        }
    }
}
