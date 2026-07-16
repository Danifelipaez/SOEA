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
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Test de integración del pipeline completo de generación (Fase 1 → Fase 2 → Fase 3 →
    /// post-chequeo 4b → persistencia), con los TRES motores reales y repositorios fake en
    /// memoria. Es la red de seguridad de las mejoras del plan de corrección/optimización:
    /// cualquier regresión en un motor o en el orquestador rompe aquí primero.
    /// </summary>
    public class GenerarHorarioServiceIntegrationTests
    {
        // ── Fakes in-memory ───────────────────────────────────────────────────────

        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            public readonly List<SOEA.Domain.Entities.Horario> Items = new();
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) =>
                Task.FromResult(Items.FirstOrDefault(h => h.Id == id));
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) =>
                Task.FromResult(Items.FirstOrDefault(h => h.Semestre == semestre));
            public Task AddAsync(SOEA.Domain.Entities.Horario horario) { Items.Add(horario); return Task.CompletedTask; }
            public Task UpdateAsync(SOEA.Domain.Entities.Horario horario) => Task.CompletedTask;
        }

        private sealed class FakeSesionRepo : ISesionRepositorio
        {
            public readonly List<Sesion> Items = new();
            public Task AddAsync(Sesion entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) { Items.AddRange(sesiones); return Task.CompletedTask; }
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Sesion entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
        }

        private sealed class FakeAsignacionRepo : IAsignacionSemanalRepositorio
        {
            public readonly List<AsignacionSemanal> Items = new();
            public Task AddAsync(AsignacionSemanal entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones) { Items.AddRange(asignaciones); return Task.CompletedTask; }
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(AsignacionSemanal entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds)
            {
                var set = sesionIds.ToHashSet();
                return Task.FromResult(Items.Where(a => set.Contains(a.SesionId)).ToList());
            }
        }

        private sealed class FakeUow : IUnitOfWork
        {
            public int Commits, Rollbacks;
            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task SaveAsync() => Task.CompletedTask;
            public Task CommitAsync() { Commits++; return Task.CompletedTask; }
            public Task RollbackAsync() { Rollbacks++; return Task.CompletedTask; }
            public void Track<T>(T entity) where T : EntidadBase { }
            public void Dispose() { }
        }

        /// <summary>Stub de Fase 3 que devuelve asignaciones deliberadamente inválidas
        /// (todas las sesiones en el mismo bloque ⇒ solapes de cohorte y de espacio),
        /// para verificar que el post-chequeo 4b hace fallback a la solución de Fase 2.</summary>
        private sealed class MotorGeneticoRoto : IMotorGenetico
        {
            public Task<ResultadoOptimizacion> OptimizarAsync(
                IEnumerable<Sesion> sesiones,
                IEnumerable<AsignacionSemanal> asignacionesFase2,
                IEnumerable<BloqueTiempo> bloques,
                IEnumerable<Espacio> espacios,
                IEnumerable<Docente> docentes,
                IEnumerable<Grupo>? grupos = null,
                ConfiguracionOptimizacion? config = null,
                IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
                IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
                IReadOnlySet<Guid>? sesionesFijasIds = null,
                CancellationToken ct = default)
            {
                var bloque0 = bloques.First();
                var esp = espacios.FirstOrDefault();
                var rotas = new List<AsignacionSemanal>();
                foreach (var s in sesiones)
                    foreach (var w in new[] { SemanaAcademica.A, SemanaAcademica.B })
                    {
                        var modalidad = SOEA.Domain.Services.ModalidadSemanal.Derivar(s, w);
                        rotas.Add(new AsignacionSemanal(
                            Guid.NewGuid(), s.Id, w, bloque0.Id,
                            modalidad == Modalidad.Presencial ? esp?.Id : null,
                            modalidad));
                    }
                return Task.FromResult(new ResultadoOptimizacion(rotas, 999m, 1, UsoFallback: false));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static GenerarHorarioService CrearServicio(
            FakeHorarioRepo horarioRepo, FakeSesionRepo sesionRepo, FakeAsignacionRepo asigRepo, FakeUow uow,
            IMotorGenetico? fase3 = null, CpSatOptions? cpSatOptions = null)
        {
            var fase1 = new AgendadorColoracionGrafo(
                new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance);
            var fase2 = new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance, cpSatOptions);
            fase3 ??= new MotorGenetico(NullLogger<MotorGenetico>.Instance);
            return new GenerarHorarioService(fase1, fase2, fase3, horarioRepo, sesionRepo, asigRepo, uow);
        }

        private static readonly Guid GrupoId = Guid.NewGuid();
        private static readonly string LabId = Guid.NewGuid().ToString();
        private static readonly string SalonId = Guid.NewGuid().ToString();

        private static GenerarHorarioRequest RequestBase() => new()
        {
            Semestre = "2026-1",
            Asignaturas = new List<AsignaturaDto>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Química Orgánica",
                    SesionesLaboratorioSemana = 1, HorasLaboratorio = 2,
                    Alternancia = "TipoA", Categoria = "Obligatoria"
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Cálculo I",
                    SesionesTeoriaPresencialSemana = 2, HorasTeoriaPresencial = 2,
                    Categoria = "Obligatoria",
                    // HC-VH: toda sesión de esta asignatura debe caer en [08:00, 12:00].
                    HoraInicioMin = "08:00", HoraFinMax = "12:00"
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Ética",
                    SesionesTeoriaVirtualSemana = 1, HorasTeoriaVirtual = 2,
                    Categoria = "Electiva"
                }
            },
            Espacios = new List<EspacioDto>
            {
                new() { Id = LabId,   Nombre = "Lab Química", Tipo = "laboratorio", Capacidad = 30 },
                new() { Id = SalonId, Nombre = "Salón 101",   Tipo = "salon",       Capacidad = 30 }
            },
            Grupos = new List<GrupoDto>
            {
                new()
                {
                    Id = GrupoId.ToString(), Nombre = "Cohorte 2026-1",
                    EstudiantesInscritos = 20,
                    Disponibilidad = new List<string> { "Matutino" }
                }
            }
        };

        private static int Hora(string hhmm) => int.Parse(hhmm.Split(':')[0]);

        /// <summary>Aserción post-hoc de HC-C01: dentro de cada semana, ninguna pareja de
        /// sesiones de la cohorte se solapa (todas las filas del run son de la misma cohorte).</summary>
        private static void AssertSinSolapesDeCohorte(IEnumerable<SesionGeneradaDto> sesiones)
        {
            foreach (var grupoSemanaDia in sesiones.GroupBy(s => (s.Semana, s.Dia)))
            {
                var spans = grupoSemanaDia
                    .Select(s => (ini: Hora(s.HoraInicio), fin: Hora(s.HoraInicio) + (int)Math.Ceiling(s.DuracionHoras), s.Id))
                    .OrderBy(x => x.ini).ToList();
                for (int i = 1; i < spans.Count; i++)
                    Assert.True(spans[i].ini >= spans[i - 1].fin,
                        $"HC-C01 violada: solape en {grupoSemanaDia.Key} entre {spans[i - 1].Id} y {spans[i].Id}.");
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HappyPath_GeneraHorarioFactible_YRespetaTodasLasHardConstraints()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var request = RequestBase();
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            // 4 sesiones (1 lab + 2 teoría presencial + 1 teoría virtual) × 2 semanas = 8 DTOs.
            Assert.Equal(8, r.Sesiones.Count);

            // HC-C01: cohorte sin solapes por semana.
            AssertSinSolapesDeCohorte(r.Sesiones);

            // HC-G01: grupo Matutino ⇒ todo inicio antes de las 12:00.
            Assert.All(r.Sesiones, s => Assert.True(Hora(s.HoraInicio) < 12,
                $"HC-G01 violada: sesión {s.Id} inicia a las {s.HoraInicio} (grupo Matutino)."));

            // HC-VH: Cálculo I confinada a [08:00, 12:00].
            var calculoId = request.Asignaturas[1].Id;
            foreach (var s in r.Sesiones.Where(s => s.AsignaturaId == calculoId))
            {
                Assert.True(Hora(s.HoraInicio) >= 8, $"HC-VH violada: inicia {s.HoraInicio} < 08:00.");
                Assert.True(Hora(s.HoraFin) <= 12, $"HC-VH violada: termina {s.HoraFin} > 12:00.");
            }

            // HC-S03 + HC-S04: laboratorio presencial en espacio Laboratorio; virtual sin espacio.
            foreach (var s in r.Sesiones)
            {
                if (s.Virtual)
                    Assert.Null(s.EspacioId);
                else if (s.TipoFlujo == nameof(TipoFlujo.Laboratorio))
                    Assert.Equal(LabId, s.EspacioId);
                else
                    Assert.NotNull(s.EspacioId);
            }

            // Regla 9 / ALT-05: el lab TipoA es presencial en A, virtual en B, misma franja.
            var labSesiones = r.Sesiones.Where(s => s.Alternancia == nameof(TipoAlternancia.TipoA)).ToList();
            Assert.Equal(2, labSesiones.Count);
            var labA = labSesiones.Single(s => s.Semana == "A");
            var labB = labSesiones.Single(s => s.Semana == "B");
            Assert.False(labA.Virtual);
            Assert.True(labB.Virtual);
            Assert.Equal(labA.HoraInicio, labB.HoraInicio);
            Assert.Equal(labA.Dia, labB.Dia);

            // Persistencia: 4 sesiones + 1 horario + 8 asignaciones, en una transacción confirmada.
            Assert.Equal(4, sesionRepo.Items.Count);
            Assert.Single(horarioRepo.Items);
            Assert.Equal(8, asigRepo.Items.Count);
            Assert.Equal(1, uow.Commits);
            Assert.Equal(0, uow.Rollbacks);
            Assert.Equal(0, horarioRepo.Items[0].ViolacionesRestriccionesDuras);
        }

        [Fact]
        public async Task GaInvalido_HaceFallbackAFase2_YPublicaHorarioValido()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow, fase3: new MotorGeneticoRoto());

            var r = await svc.EjecutarAsync(RequestBase());

            // El stub devolvió solapes; el post-chequeo 4b debe descartar su salida.
            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Contains(r.Logs, l => l.Contains("[WARN]") && l.Contains("Fase 2"));
            Assert.Equal(0m, r.PuntajeFitness);
            // Lo publicado (la solución de Fase 2) sigue siendo válido.
            AssertSinSolapesDeCohorte(r.Sesiones);
            Assert.Equal(0, horarioRepo.Items[0].ViolacionesRestriccionesDuras);
        }

        [Fact]
        public async Task SinEspacios_ConDemandaPresencial_RetornaInfactibleSinPersistir()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var request = RequestBase();
            request.Espacios = new List<EspacioDto>(); // demanda presencial > capacidad (0)

            var r = await svc.EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.False(string.IsNullOrEmpty(r.MensajeError));
            Assert.Empty(r.Sesiones);
            // Nada se persistió.
            Assert.Empty(horarioRepo.Items);
            Assert.Empty(sesionRepo.Items);
            Assert.Empty(asigRepo.Items);
            Assert.Equal(0, uow.Commits);
        }

        [Fact]
        public async Task SesionFijaConHoraFueraDeGrilla_SeOmiteConWarning_YPipelineSigue()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var request = RequestBase();
            request.SesionesFijas = new List<SesionFijaDto>
            {
                // "07:30" no coincide con ningún bloque de la grilla canónica (bloques en punto).
                new() { AsignaturaId = request.Asignaturas[0].Id, Dia = "lunes", HoraInicio = "07:30", DuracionHoras = 2m }
            };

            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Equal(1, r.SesionesFijasOmitidas);
            Assert.Contains(r.Logs, l => l.Contains("[WARN]") && l.Contains("Sesión fija omitida"));
            // La sesión omitida no se coló en el horario publicado ni en la persistencia.
            AssertSinSolapesDeCohorte(r.Sesiones);
        }

        // B4: la Semilla del DTO ahora llega al GA (antes se descartaba en MapearConfiguracion),
        // así que dos ejecuciones del pipeline completo con la misma entrada y semilla producen
        // el mismo horario. NumWorkers:1 fija también a CP-SAT (Fase 2) para que el determinismo
        // no dependa del scheduling de sus workers paralelos.
        [Fact]
        public async Task ConSemillaFija_ElPipelineCompletoEsReproducible()
        {
            var cpSatOptions = new CpSatOptions { NumWorkers = 1 };
            var request = RequestBase();
            request.Configuracion = new ConfiguracionAlgoritmoDto { Semilla = 2026 };

            var svc1 = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow(),
                cpSatOptions: cpSatOptions);
            var r1 = await svc1.EjecutarAsync(request);

            var svc2 = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow(),
                cpSatOptions: cpSatOptions);
            var r2 = await svc2.EjecutarAsync(request);

            Assert.True(r1.EsFactible && r2.EsFactible);
            Assert.Equal(r1.PuntajeFitness, r2.PuntajeFitness);
            var horario1 = r1.Sesiones.Select(s => (s.AsignaturaId, s.Semana, s.Dia, s.HoraInicio, s.Virtual)).OrderBy(x => x.AsignaturaId).ThenBy(x => x.Semana).ToList();
            var horario2 = r2.Sesiones.Select(s => (s.AsignaturaId, s.Semana, s.Dia, s.HoraInicio, s.Virtual)).OrderBy(x => x.AsignaturaId).ThenBy(x => x.Semana).ToList();
            Assert.Equal(horario1, horario2);
        }
    }
}
