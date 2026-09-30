using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.GraphColoring;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// L-10 (auditoría 2026-09-28): cada solve de CP-SAT (más la relajación y el barrido diagnóstico) podía
    /// gastar su propio timeout completo, y el presupuesto del bucle de cesión solo se miraba entre
    /// iteraciones; el peor caso pasaba de los 2 000 s dentro de un HTTP síncrono que Azure corta a los
    /// 230 s. <see cref="GenerarHorarioService"/> ahora comparte UN plazo entre las tres fases.
    /// </summary>
    public class GenerarHorarioPlazoTotalTests
    {
        /// <summary>Fase 2 que nunca termina por sí sola: solo acaba cuando se cancela su token (como un solve
        /// de CP-SAT interrumpido con StopSearch).</summary>
        private sealed class Fase2QueNoTermina : IMotorConstraintProgramming
        {
            public bool RecibioToken;
            public async Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, IEnumerable<Guid>? sesionesFijasIds = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                CancellationToken ct = default)
            {
                RecibioToken = ct.CanBeCanceled;
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("inalcanzable");
            }
        }

        /// <summary>Fase 3 que nunca termina por sí sola (un GA con población y generaciones enormes).</summary>
        private sealed class Fase3QueNoTermina : IMotorGenetico
        {
            public async Task<ResultadoOptimizacion> OptimizarAsync(
                IEnumerable<Sesion> sesiones, IEnumerable<AsignacionSemanal> asignacionesFase2,
                IEnumerable<BloqueTiempo> bloques, IEnumerable<Espacio> espacios,
                IEnumerable<Grupo>? grupos = null, ConfiguracionOptimizacion? config = null,
                IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                IReadOnlySet<Guid>? sesionesFijasIds = null, IReadOnlyList<Guid>? sesionesCedidasParaRevertir = null,
                CancellationToken ct = default)
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("inalcanzable");
            }
        }

        private sealed record Montaje(GenerarHorarioService Servicio, FakeSesionRepo Sesiones, FakeAsignacionRepo Asignaciones, FakeUnitOfWork Uow);

        private static Montaje Montar(IMotorConstraintProgramming fase2, IMotorGenetico fase3, TimeSpan plazo)
        {
            var fase1 = new AgendadorColoracionGrafo(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance);
            var sesiones = new FakeSesionRepo();
            var asignaciones = new FakeAsignacionRepo();
            var uow = new FakeUnitOfWork();
            var criterios = new FakeCriterioCesionRepo(
                new CriterioCesionAlternancia(CriterioCesionAlternancia.IdMultiplesSesiones, CriterioElegibilidadAlternancia.MultiplesSesiones, 1),
                new CriterioCesionAlternancia(CriterioCesionAlternancia.IdElectiva, CriterioElegibilidadAlternancia.Electiva, 2),
                new CriterioCesionAlternancia(CriterioCesionAlternancia.IdOptativa, CriterioElegibilidadAlternancia.Optativa, 3),
                new CriterioCesionAlternancia(CriterioCesionAlternancia.IdElegible, CriterioElegibilidadAlternancia.Elegible, 4));
            var servicio = new GenerarHorarioService(fase1, fase2, fase3, new FakeHorarioRepo(), sesiones, asignaciones,
                new FakeGrupoRepo(), criterios, uow, plazoTotal: plazo);
            return new Montaje(servicio, sesiones, asignaciones, uow);
        }

        private static GenerarHorarioRequest Request()
        {
            var asigId = Guid.NewGuid().ToString();
            return new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new() { Id = asigId, Nombre = "Física", SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2, Categoria = "Obligatoria" }
                },
                Espacios = new List<EspacioDto> { new() { Id = Guid.NewGuid().ToString(), Nombre = "Salón 1", Tipo = "salon", Capacidad = 30 } },
                Grupos = new List<GrupoDto> { new() { Id = Guid.NewGuid().ToString(), Nombre = "Grupo Física", AsignaturaId = asigId, EstudiantesInscritos = 20 } }
            };
        }

        private static MotorConstraintProgramming Fase2Real() =>
            new(NullLogger<MotorConstraintProgramming>.Instance, new CpSatOptions { NumWorkers = 1 });

        /// <summary>Espera con guarda: sin el plazo, estos dobles no terminan nunca y la prueba se colgaría.</summary>
        private static async Task<GenerarHorarioResponse> EsperarConGuardaAsync(Task<GenerarHorarioResponse> tarea)
        {
            var terminoATiempo = await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromSeconds(30))) == tarea;
            Assert.True(terminoATiempo, "la generación no respetó el plazo total"); // antes: esperaba al timeout del solver / a un GA sin fin
            return await tarea;
        }

        [Fact]
        public async Task PlazoAgotadoEnFase2_ResponderTimeoutClaro_YNoPersisteNada()
        {
            var fase2 = new Fase2QueNoTermina();
            var m = Montar(fase2, new Fase3QueNoTermina(), plazo: TimeSpan.FromMilliseconds(300));

            var r = await EsperarConGuardaAsync(m.Servicio.EjecutarAsync(Request()));

            Assert.True(fase2.RecibioToken);
            Assert.False(r.EsFactible);
            Assert.Null(r.HorarioId);
            Assert.Equal(nameof(MotivoInfactibilidad.Timeout), r.MotivoInfactibilidad);
            Assert.Contains("tiempo máximo", r.MensajeError);
            Assert.Empty(await m.Sesiones.GetAllAsync());
            Assert.Empty(await m.Asignaciones.GetAllAsync());
            Assert.Equal(0, m.Uow.Commits);
        }

        [Fact]
        public async Task PlazoAgotadoEnFase3_PublicaLaSolucionDeFase2_SinOptimizar()
        {
            // Fase 1 y 2 reales (una instancia mínima resuelve en milisegundos); el plazo vence dentro de la Fase 3.
            var m = Montar(Fase2Real(), new Fase3QueNoTermina(), plazo: TimeSpan.FromSeconds(5));

            var r = await EsperarConGuardaAsync(m.Servicio.EjecutarAsync(Request()));

            Assert.True(r.EsFactible, r.MensajeError);
            Assert.Single(r.Sesiones);
            Assert.Contains(r.Logs, l => l.Contains("plazo total", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, m.Uow.Commits);
            Assert.Single(await m.Asignaciones.GetAllAsync());
        }

        [Fact]
        public async Task CancelacionDelCliente_SigueSiendoCancelacion_NoSeDisfrazaDeTimeout()
        {
            var m = Montar(new Fase2QueNoTermina(), new Fase3QueNoTermina(), plazo: TimeSpan.FromMinutes(5));
            using var cliente = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => m.Servicio.EjecutarAsync(Request(), cliente.Token));
        }
    }
}
