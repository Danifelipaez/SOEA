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

        private static Sesion Sesion(Guid docenteId, TipoAlternancia alt, Modalidad modalidad, decimal dur,
            TipoFlujo tipoFlujo = TipoFlujo.Laboratorio) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, Guid.NewGuid(), null, null, alt, modalidad, dur, false, false,
                tipoFlujo: tipoFlujo);

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

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

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

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

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

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

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

            var r1 = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg(99));
            var r2 = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg(99));

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

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

            Assert.True(r.UsoFallback);
            Assert.Equal(fase2.Count, r.AsignacionesOptimizadas.Count);
        }

        [Fact]
        public async Task Laboratorio_ConSoloEspacioSalon_HaceFallback()
        {
            // HC-S03: una sesión TipoFlujo.Laboratorio exige un espacio tipo Laboratorio.
            // Con un único Salon disponible, no hay candidato válido → fallback a Fase 2.
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m,
                tipoFlujo: TipoFlujo.Laboratorio) };
            var bloques  = Grilla(6);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Salon", TipoEspacio.Salon, 30) };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

            Assert.True(r.UsoFallback);
        }

        [Fact]
        public async Task TeoriaPresencial_ConSoloEspacioSalon_Exito()
        {
            // Una sesión TipoFlujo.AulaVirtual (teoría presencial) no exige laboratorio: un Salon
            // le basta. Antes del fix de HC-S03 en AsignadorEspacios, el heurístico viejo dependía
            // de un EspacioId previo (siempre null pre-generación) y este caso ya "pasaba" por
            // accidente; ahora pasa por la razón correcta (TipoFlujo).
            var docId = Guid.NewGuid();
            var sesiones = new List<Sesion> { Sesion(docId, TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m,
                tipoFlujo: TipoFlujo.AulaVirtual) };
            var bloques  = Grilla(6);
            var docentes = new List<Docente> { Doc(docId, bloques) };
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Salon", TipoEspacio.Salon, 30) };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, docentes, config: Cfg());

            Assert.False(r.UsoFallback);
            Assert.All(r.AsignacionesOptimizadas.Where(a => a.Modalidad == Modalidad.Presencial),
                a => Assert.NotNull(a.EspacioId));
        }

        // HC-S05: si la sesión trae espacio fijo, el pase de aulas del GA debe usar ESE espacio,
        // no el primero libre (mismo criterio que CP-SAT en Fase 2).
        [Fact]
        public async Task EspacioFijo_ElPaseDeAulasLoRespeta()
        {
            var espacioA = new Espacio(Guid.NewGuid(), "Salón A", TipoEspacio.Salon, 30);
            var espacioB = new Espacio(Guid.NewGuid(), "Salón B", TipoEspacio.Salon, 30);
            var espacios = new List<Espacio> { espacioA, espacioB };
            var bloques  = Grilla(6);

            // Teoría presencial con espacio fijo = B (el greedy sin HC-S05 elegiría A, el primero).
            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), espacioB.Id, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);
            var sesiones = new List<Sesion> { sesion };
            var fase2 = new List<AsignacionSemanal>
            {
                new(Guid.NewGuid(), sesion.Id, SemanaAcademica.A, bloques[0].Id, espacioB.Id, Modalidad.Presencial),
                new(Guid.NewGuid(), sesion.Id, SemanaAcademica.B, bloques[0].Id, espacioB.Id, Modalidad.Presencial),
            };

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(), config: Cfg());

            Assert.False(r.UsoFallback);
            Assert.All(r.AsignacionesOptimizadas.Where(a => a.Modalidad == Modalidad.Presencial),
                a => Assert.Equal(espacioB.Id, a.EspacioId));
        }

        // HC-CAP: espacio con aforo insuficiente para el grupo no es candidato. Con un único
        // espacio demasiado chico, el pase de aulas es infactible → fallback a Fase 2.
        [Fact]
        public async Task AforoInsuficiente_HaceFallbackAFase2()
        {
            var grupoId  = Guid.NewGuid();
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Salón chico", TipoEspacio.Salon, 10) };
            var bloques  = Grilla(6);
            var grupo = new Grupo(grupoId, "Cohorte", Guid.Empty, estudiantesInscritos: 40,
                disponibilidad: new List<FranjaHoraria>());

            var sesion = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.AulaVirtual);
            var sesiones = new List<Sesion> { sesion };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                grupos: new List<Grupo> { grupo }, config: Cfg());

            Assert.True(r.UsoFallback);
        }

        // B1: el GA pasó de steady-state (1 hijo/generación) a (μ+λ) con elitismo — cada
        // generación produce TamañoPoblacion hijos y sobreviven los mejores. El elitismo
        // garantiza que el mejor fitness es MONÓTONO NO CRECIENTE con más generaciones: correr
        // más generaciones nunca puede terminar peor que correr menos, con la misma semilla
        // (misma secuencia de números aleatorios hasta el punto en que divergen).
        [Fact]
        public async Task MasGeneraciones_NuncaEmpeoraElFitness_ComparadoConMenos()
        {
            var grupoId  = Guid.NewGuid();
            var bloques  = Grilla(10); // un día, 10 bloques (07:00–17:00)
            var espacios = new List<Espacio>();
            // 3 sesiones virtuales de la misma cohorte, sembradas con huecos grandes entre sí
            // (0, 4, 8): hay margen real de mejora en SC-01 (huecos) para más generaciones.
            var sesiones = Enumerable.Range(0, 3)
                .Select(_ => new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                    TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false))
                .ToList();
            var fase2 = Fase2(sesiones, new[] { 0, 4, 8 }, bloques, espacios);

            ConfiguracionOptimizacion Cfg(int maxGen) => new(
                TamañoPoblacion: 20, MaxGeneraciones: maxGen, UmbralConvergencia: 1000, Semilla: 42);

            var pocasGen = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                grupos: null, config: Cfg(1));
            var masGen = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                grupos: null, config: Cfg(50));

            Assert.True(masGen.PuntajeFitness <= pocasGen.PuntajeFitness,
                $"50 generaciones dio fitness {masGen.PuntajeFitness}, peor que 1 generación ({pocasGen.PuntajeFitness}).");
        }
    }
}
