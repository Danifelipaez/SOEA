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
    /// Pase de reversión post-Fase 3: tras el GA y la asignación real de aulas, intenta recuperar
    /// a presencial cada sesión marcada <c>CedidaPorSaturacion</c> (en orden inverso de cesión),
    /// validando contra el empaque REAL de <see cref="AsignadorEspacios"/> — más preciso que el
    /// pre-check agregado de Fase 2.
    /// </summary>
    public class ReversionPostFase3Tests
    {
        private static readonly MotorGenetico Motor = new(NullLogger<MotorGenetico>.Instance);

        private static List<BloqueTiempo> Grilla(int n) =>
            Enumerable.Range(0, n)
                .Select(h => new BloqueTiempo(Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)))
                .ToList();

        private static Sesion SesionLab(TipoAlternancia alt, bool cedidaPorSaturacion, bool bloqueada = false)
        {
            // Construye SinAlternancia y, si aplica, pasa por AplicarAlternancia para marcar
            // CedidaPorSaturacion=true de forma realista (el flag solo lo setean los mutadores,
            // nunca el constructor).
            var s = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false,
                tipoFlujo: TipoFlujo.Laboratorio, bloqueada: bloqueada);
            if (alt != TipoAlternancia.SinAlternancia || cedidaPorSaturacion)
                s.AplicarAlternancia(alt, cedidaPorSaturacion: cedidaPorSaturacion);
            return s;
        }

        private static List<AsignacionSemanal> Fase2(List<Sesion> sesiones, int[] inicio, List<BloqueTiempo> bloques, List<Espacio> espacios)
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

        private static ConfiguracionOptimizacion Cfg() => new(TamañoPoblacion: 10, MaxGeneraciones: 10, Semilla: 42);

        [Fact]
        public async Task SesionCedida_SeRevierteAPresencial_CuandoElEmpaqueRealLoPermite()
        {
            // TipoA cedida por saturación: presencial en A, virtual en B. Con 2 labs libres y sin
            // competencia, revertir a presencial puro (ambas semanas) sí cabe.
            var s1 = SesionLab(TipoAlternancia.TipoA, cedidaPorSaturacion: true);
            var sesiones = new List<Sesion> { s1 };
            var bloques  = Grilla(4);
            var espacios = new List<Espacio>
            {
                new(Guid.NewGuid(), "Lab1", TipoEspacio.Laboratorio, 30),
                new(Guid.NewGuid(), "Lab2", TipoEspacio.Laboratorio, 30)
            };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                config: Cfg(), sesionesCedidasParaRevertir: new List<Guid> { s1.Id });

            Assert.False(r.UsoFallback);
            Assert.NotNull(r.SesionesRevertidasIds);
            Assert.Contains(s1.Id, r.SesionesRevertidasIds!);

            // Ambas semanas quedan presenciales con aula asignada (revertido, ya no alterna).
            var a = r.AsignacionesOptimizadas.Single(x => x.SesionId == s1.Id && x.Semana == SemanaAcademica.A);
            var b = r.AsignacionesOptimizadas.Single(x => x.SesionId == s1.Id && x.Semana == SemanaAcademica.B);
            Assert.Equal(Modalidad.Presencial, a.Modalidad);
            Assert.Equal(Modalidad.Presencial, b.Modalidad);
            Assert.NotNull(a.EspacioId);
            Assert.NotNull(b.EspacioId);
        }

        [Fact]
        public async Task SesionCedida_PermaneceCedida_CuandoNoHayEspacioRealParaRevertir()
        {
            // Un solo lab, compartido por un TipoA (cedido) y su pareja TipoB (fija, no cedida) en
            // el mismo bloque — exactamente el patrón "compartir lab entre semanas distintas".
            // Revertir el TipoA a presencial puro chocaría con el TipoB en la Semana B: debe
            // quedarse cedido.
            var s1 = SesionLab(TipoAlternancia.TipoA, cedidaPorSaturacion: true);
            var s2 = SesionLab(TipoAlternancia.TipoB, cedidaPorSaturacion: false);
            var sesiones = new List<Sesion> { s1, s2 };
            var bloques  = Grilla(4);
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Lab1", TipoEspacio.Laboratorio, 30) };
            var fase2 = Fase2(sesiones, new[] { 0, 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                config: Cfg(), sesionesCedidasParaRevertir: new List<Guid> { s1.Id });

            Assert.False(r.UsoFallback);
            Assert.True(r.SesionesRevertidasIds is null || !r.SesionesRevertidasIds.Contains(s1.Id));

            // s1 sigue alternando: presencial en A, virtual en B (rollback exacto, no estado roto).
            var b = r.AsignacionesOptimizadas.Single(x => x.SesionId == s1.Id && x.Semana == SemanaAcademica.B);
            Assert.Equal(Modalidad.Virtual, b.Modalidad);
            Assert.Null(b.EspacioId);
        }

        [Fact]
        public async Task SesionBloqueada_NuncaSeRevierte_AunqueEsteEnLaListaDeCedidas()
        {
            var s1 = SesionLab(TipoAlternancia.SinAlternancia, cedidaPorSaturacion: false);
            s1.VirtualizarSesion(cedidaPorSaturacion: true); // estado artificial: cedida
            s1.Bloquear();                                    // ...pero bloqueada

            var sesiones = new List<Sesion> { s1 };
            var bloques  = Grilla(4);
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Lab1", TipoEspacio.Laboratorio, 30) };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(),
                config: Cfg(), sesionesCedidasParaRevertir: new List<Guid> { s1.Id });

            Assert.False(r.UsoFallback);
            Assert.True(r.SesionesRevertidasIds is null || !r.SesionesRevertidasIds.Contains(s1.Id));
            Assert.All(r.AsignacionesOptimizadas.Where(a => a.SesionId == s1.Id),
                a => Assert.Equal(Modalidad.Virtual, a.Modalidad));
        }

        [Fact]
        public async Task SesionesCedidasParaRevertirNull_EsNoOp()
        {
            var s1 = SesionLab(TipoAlternancia.SinAlternancia, cedidaPorSaturacion: false);
            var sesiones = new List<Sesion> { s1 };
            var bloques  = Grilla(4);
            var espacios = new List<Espacio> { new(Guid.NewGuid(), "Lab1", TipoEspacio.Laboratorio, 30) };
            var fase2 = Fase2(sesiones, new[] { 0 }, bloques, espacios);

            var r = await Motor.OptimizarAsync(sesiones, fase2, bloques, espacios, new List<Docente>(), config: Cfg());

            Assert.False(r.UsoFallback);
            Assert.Null(r.SesionesRevertidasIds);
        }
    }
}
