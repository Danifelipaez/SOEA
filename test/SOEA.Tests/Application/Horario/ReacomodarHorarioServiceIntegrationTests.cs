using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Petición 13 (P5): mover una sesión ya generada no debe recalcular el resto del horario,
    /// salvo las sesiones con las que ahora choca. Genera un horario real de 2 grupos
    /// independientes con GenerarHorarioService, y verifica que ReacomodarHorarioService mueve
    /// solo lo que corresponde.
    /// </summary>
    public class ReacomodarHorarioServiceIntegrationTests
    {
        // ── Fakes in-memory (mismo patrón que GenerarHorarioServiceIntegrationTests) ──

        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            public readonly List<SOEA.Domain.Entities.Horario> Items = new();
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) =>
                Task.FromResult(Items.FirstOrDefault(h => h.Id == id));
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) =>
                Task.FromResult(Items.FirstOrDefault(h => h.Semestre == semestre));
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllBySemestreAsync(string semestre) => Task.FromResult(Items.Where(h => h.Semestre == semestre).ToList());
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
            public Task UpdateAsync(Sesion entity) { Items.RemoveAll(s => s.Id == entity.Id); Items.Add(entity); return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
            public Task DeleteRangeAsync(IEnumerable<Guid> ids) { var set = ids.ToHashSet(); Items.RemoveAll(s => set.Contains(s.Id)); return Task.CompletedTask; }
            public Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids) { var set = ids.ToHashSet(); return Task.FromResult(Items.Where(s => set.Contains(s.Id)).ToList()); }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
            public Task<List<Guid>> GetIdsByGrupoIdAsync(Guid grupoId) => Task.FromResult(new List<Guid>());
            public Task<List<Guid>> GetIdsByAsignaturaIdAsync(Guid asignaturaId) => Task.FromResult(new List<Guid>());
            public Task<List<Guid>> GetIdsByEspacioIdAsync(Guid espacioId) => Task.FromResult(new List<Guid>());
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
            public Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds) { var set = sesionIds.ToHashSet(); Items.RemoveAll(a => set.Contains(a.SesionId)); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds)
            {
                var set = sesionIds.ToHashSet();
                return Task.FromResult(Items.Where(a => set.Contains(a.SesionId)).ToList());
            }
        }

        private sealed class FakeCriterioCesionRepo : ICriterioCesionAlternanciaRepositorio
        {
            public readonly List<CriterioCesionAlternancia> Items = new()
            {
                new(CriterioCesionAlternancia.IdMultiplesSesiones, CriterioElegibilidadAlternancia.MultiplesSesiones, 1),
                new(CriterioCesionAlternancia.IdElectiva, CriterioElegibilidadAlternancia.Electiva, 2),
                new(CriterioCesionAlternancia.IdOptativa, CriterioElegibilidadAlternancia.Optativa, 3),
                new(CriterioCesionAlternancia.IdElegible, CriterioElegibilidadAlternancia.Elegible, 4)
            };
            public Task AddAsync(CriterioCesionAlternancia entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task<CriterioCesionAlternancia?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));
            public Task<List<CriterioCesionAlternancia>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(CriterioCesionAlternancia entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(c => c.Id == id); return Task.CompletedTask; }
        }

        private sealed class FakeUow : IUnitOfWork
        {
            public int Commits, Rollbacks;
            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task SaveAsync() => Task.CompletedTask;
            public Task CommitAsync() { Commits++; return Task.CompletedTask; }
            public Task RollbackAsync() { Rollbacks++; return Task.CompletedTask; }
            public void Track<T>(T entity) where T : EntidadBase { }
            public void AttachUnchanged<T>(T entity) where T : EntidadBase { }
            public void Dispose() { }
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            public readonly List<Grupo> Items = new();
            public Task AddAsync(Grupo entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(g => g.Id == id));
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Grupo entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Grupo?>(null);
            public Task<Grupo?> GetByCodigoAsync(string codigo) => Task.FromResult<Grupo?>(null);
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) =>
                Task.FromResult(Items.Where(g => g.AsignaturaId == asignaturaId));
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) =>
                Task.FromResult(Items.Where(g => g.DocenteId == docenteId));
        }

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            public readonly List<Asignatura> Items = new();
            public Task AddAsync(Asignatura entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task<Asignatura?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Asignatura entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Asignatura?> GetByCodigoAsync(string codigo) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Asignatura?>(null);
        }

        private sealed class FakeDocenteRepo : IDocenteRepositorio
        {
            public readonly List<Docente> Items = new();
            public Task AddAsync(Docente entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task<Docente?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));
            public Task<List<Docente>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Docente entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Docente?> GetByCedulaAsync(string cedula) => Task.FromResult<Docente?>(null);
            public Task<Docente?> GetByNombreAsync(string nombre) => Task.FromResult<Docente?>(null);
        }

        private sealed class FakeEspacioRepo : IEspacioRepositorio
        {
            public readonly List<Espacio> Items = new();
            public Task AddAsync(Espacio entity) { Items.Add(entity); return Task.CompletedTask; }
            public Task<Espacio?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(e => e.Id == id));
            public Task<List<Espacio>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Espacio entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Espacio?> GetByNombreAsync(string nombre) => Task.FromResult<Espacio?>(null);
        }

        // ── Fixture: 2 asignaturas independientes (1 grupo cada una, 1 sesión de teoría
        // presencial cada una, sin alternancia/virtual) — así cualquier choque solo puede venir
        // del movimiento que el test provoca a propósito, no de la generación inicial.

        private readonly Guid _asigAId = Guid.NewGuid();
        private readonly Guid _asigBId = Guid.NewGuid();
        private readonly Guid _salon1Id = Guid.NewGuid();
        private readonly Guid _salon2Id = Guid.NewGuid();
        private readonly Guid _grupoAId = Guid.NewGuid();
        private readonly Guid _grupoBId = Guid.NewGuid();
        private readonly Guid _programaId = Guid.NewGuid();

        private GenerarHorarioRequest RequestBase() => new()
        {
            Semestre = "2026-1",
            Asignaturas = new List<AsignaturaDto>
            {
                new() { Id = _asigAId.ToString(), Nombre = "Asignatura A", SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2, Categoria = "Obligatoria" },
                new() { Id = _asigBId.ToString(), Nombre = "Asignatura B", SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2, Categoria = "Obligatoria" }
            },
            Espacios = new List<EspacioDto>
            {
                new() { Id = _salon1Id.ToString(), Nombre = "Salón 1", Tipo = "salon", Capacidad = 30 },
                new() { Id = _salon2Id.ToString(), Nombre = "Salón 2", Tipo = "salon", Capacidad = 30 }
            },
            Grupos = new List<GrupoDto>
            {
                new() { Id = _grupoAId.ToString(), Nombre = "Grupo A", AsignaturaId = _asigAId.ToString(), EstudiantesInscritos = 20 },
                new() { Id = _grupoBId.ToString(), Nombre = "Grupo B", AsignaturaId = _asigBId.ToString(), EstudiantesInscritos = 20 }
            }
        };

        private static GenerarHorarioService CrearGenerarServicio(
            FakeHorarioRepo horarioRepo, FakeSesionRepo sesionRepo, FakeAsignacionRepo asigRepo, FakeUow uow)
        {
            var fase1 = new AgendadorColoracionGrafo(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance);
            var fase2 = new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance, new CpSatOptions { NumWorkers = 1 });
            var fase3 = new MotorGenetico(NullLogger<MotorGenetico>.Instance,
                new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance));
            return new GenerarHorarioService(fase1, fase2, fase3, horarioRepo, sesionRepo, asigRepo, new FakeGrupoRepo(), new FakeCriterioCesionRepo(), uow);
        }

        private ReacomodarHorarioService CrearReacomodarServicio(
            FakeHorarioRepo horarioRepo, FakeSesionRepo sesionRepo, FakeAsignacionRepo asigRepo, FakeUow uow)
        {
            var grupoRepo = new FakeGrupoRepo
            {
                Items =
                {
                    new Grupo(_grupoAId, "Grupo A", Guid.Empty, 20, asignaturaId: _asigAId),
                    new Grupo(_grupoBId, "Grupo B", Guid.Empty, 20, asignaturaId: _asigBId)
                }
            };
            var asignaturaRepo = new FakeAsignaturaRepo
            {
                Items =
                {
                    new Asignatura(_asigAId, "Asignatura A", "A-1", 1, 2, 0, 0, 0, 0, 0, _programaId),
                    new Asignatura(_asigBId, "Asignatura B", "B-1", 1, 2, 0, 0, 0, 0, 0, _programaId)
                }
            };
            var espacioRepo = new FakeEspacioRepo
            {
                Items =
                {
                    new Espacio(_salon1Id, "Salón 1", TipoEspacio.Salon, 30),
                    new Espacio(_salon2Id, "Salón 2", TipoEspacio.Salon, 30)
                }
            };
            var docenteRepo = new FakeDocenteRepo();
            var fase2 = new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance, new CpSatOptions { NumWorkers = 1 });
            return new ReacomodarHorarioService(
                horarioRepo, sesionRepo, asigRepo, espacioRepo, docenteRepo, grupoRepo, asignaturaRepo, fase2, uow);
        }

        [Fact]
        public async Task MoverSesionAUnSlotLibre_NoTocaLasDemasSesiones()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();

            var genSvc = CrearGenerarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            var genResult = await genSvc.EjecutarAsync(RequestBase());
            Assert.True(genResult.EsFactible, genResult.MensajeError ?? string.Join("\n", genResult.Logs));

            var sesionA = sesionRepo.Items.Single(s => s.AsignaturaId == _asigAId);
            var sesionBAntes = genResult.Sesiones.Where(s => s.AsignaturaId == _asigBId.ToString())
                .Select(s => (s.Semana, s.Dia, s.HoraInicio, s.EspacioId)).OrderBy(x => x.Semana).ToList();

            var reacomodarSvc = CrearReacomodarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            var resultado = await reacomodarSvc.EjecutarAsync(new ReacomodarHorarioRequest
            {
                HorarioId = horarioRepo.Items[0].Id,
                SesionEditadaId = sesionA.Id,
                Dia = "viernes",
                HoraInicio = "20:00"
            });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            Assert.Empty(resultado.Advertencias); // nada chocó: no hubo que invocar CP-SAT

            var sesionADespues = resultado.Sesiones.Where(s => s.Id == sesionA.Id.ToString()).ToList();
            Assert.Single(sesionADespues); // una fila: aplica a todas las semanas
            Assert.All(sesionADespues, s => Assert.Equal("viernes", s.Dia));
            Assert.All(sesionADespues, s => Assert.Equal("20:00", s.HoraInicio));

            var sesionBDespues = resultado.Sesiones.Where(s => s.AsignaturaId == _asigBId.ToString())
                .Select(s => (s.Semana, s.Dia, s.HoraInicio, s.EspacioId)).OrderBy(x => x.Semana).ToList();
            Assert.Equal(sesionBAntes, sesionBDespues); // Grupo B: exactamente igual que antes del movimiento
        }

        [Fact]
        public async Task MoverSesionSobreOtraQueChoca_LiberaSoloLaQueChoca_YReubicaAmbasSinSolape()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();

            var genSvc = CrearGenerarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            var genResult = await genSvc.EjecutarAsync(RequestBase());
            Assert.True(genResult.EsFactible, genResult.MensajeError ?? string.Join("\n", genResult.Logs));

            var sesionA = sesionRepo.Items.Single(s => s.AsignaturaId == _asigAId);
            var sesionB = sesionRepo.Items.Single(s => s.AsignaturaId == _asigBId);
            var dtoB = genResult.Sesiones.First(s => s.AsignaturaId == _asigBId.ToString());

            var reacomodarSvc = CrearReacomodarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            // Mover A exactamente sobre el slot + espacio actual de B ⇒ choca por espacio.
            var resultado = await reacomodarSvc.EjecutarAsync(new ReacomodarHorarioRequest
            {
                HorarioId = horarioRepo.Items[0].Id,
                SesionEditadaId = sesionA.Id,
                Dia = dtoB.Dia,
                HoraInicio = dtoB.HoraInicio,
                EspacioId = dtoB.EspacioId
            });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            Assert.Single(resultado.Advertencias); // B se liberó y reubicó

            var filasA = resultado.Sesiones.Where(s => s.Id == sesionA.Id.ToString()).ToList();
            Assert.All(filasA, s => Assert.Equal(dtoB.Dia, s.Dia));
            Assert.All(filasA, s => Assert.Equal(dtoB.HoraInicio, s.HoraInicio));
            Assert.All(filasA, s => Assert.Equal(dtoB.EspacioId, s.EspacioId));

            var filasB = resultado.Sesiones.Where(s => s.Id == sesionB.Id.ToString()).ToList();
            Assert.Single(filasB);
            // B ya no puede seguir en el mismo (día, hora, espacio) que A: eso era justo el choque.
            Assert.All(filasB, s => Assert.False(s.Dia == dtoB.Dia && s.HoraInicio == dtoB.HoraInicio && s.EspacioId == dtoB.EspacioId));
        }

        // ── Fixture directo (sin pasar por GenerarHorarioService): da control total sobre qué
        // aula queda en la AsignacionSemanal de cada sesión frente a cuál queda en Sesion.EspacioId
        // — la distinción exacta que estos dos bugs necesitan reproducir. Sesion.EspacioId es el
        // requisito de aula FIJA (HC-S05); casi siempre null, incluso para una sesión presencial
        // con aula real asignada por CP-SAT — esa vive solo en su AsignacionSemanal.

        private static BloqueTiempo BloqueReal(DiaDeSemana dia, int hora) =>
            GrillaInstitucional.GenerarBloques().Single(b => b.Dia == dia && b.HoraInicio == new TimeOnly(hora, 0));

        private Sesion SesionSinAulaFija(Guid asignaturaId, Guid grupoId, Guid bloqueId) =>
            new(Guid.NewGuid(), asignaturaId, docenteId: null, bloqueId, espacioId: null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, duracionHoras: 2m, esBloque: false, estaDividida: false,
                tipoFlujo: TipoFlujo.AulaVirtual); // teoría presencial ⇒ cualquier Salón sirve (no exige Laboratorio)

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.5 — bug 1/2): mover una sesión sin volver
        /// a especificar EspacioId en la petición ("no tocar el aula actual") reconstruía la fila
        /// nueva desde Sesion.EspacioId en vez de desde la AsignacionSemanal actual — para
        /// cualquier sesión sin aula FIJA (la inmensa mayoría), eso es null, así que el aula real
        /// desaparecía en silencio con EsFactible=true. Habría fallado antes del fix
        /// (EspacioId nulo en la respuesta en vez de Salón 1).
        /// </summary>
        [Fact]
        public async Task MoverSesionSinReespecificarEspacio_ConservaElAulaQueYaTenia()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();

            var bloqueLunes  = BloqueReal(DiaDeSemana.Lunes, 8);
            var bloqueMartes = BloqueReal(DiaDeSemana.Martes, 8);

            var sesionA = SesionSinAulaFija(_asigAId, _grupoAId, bloqueLunes.Id);
            sesionRepo.Items.Add(sesionA);
            asigRepo.Items.Add(new AsignacionSemanal(Guid.NewGuid(), sesionA.Id, SemanaAcademica.A, bloqueLunes.Id, _salon1Id, Modalidad.Presencial));
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { sesionA.Id });
            horarioRepo.Items.Add(horario);

            var reacomodarSvc = CrearReacomodarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            var resultado = await reacomodarSvc.EjecutarAsync(new ReacomodarHorarioRequest
            {
                HorarioId = horario.Id,
                SesionEditadaId = sesionA.Id,
                Dia = "martes",
                HoraInicio = "08:00"
                // EspacioId se omite a propósito: "no tocar el aula actual".
            });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            var fila = Assert.Single(resultado.Sesiones);
            Assert.Equal(_salon1Id.ToString(), fila.EspacioId);
        }

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.5 — bug 2/2): el detector de choques solo
        /// comparaba contra `espacioNuevo` (el de la petición). Si la petición no trae uno ("no
        /// tocar el aula actual"), nunca comprobaba si la sesión editada, quedándose en SU PROPIA
        /// aula, colisionaba con otra sesión que ya estaba ahí en la nueva franja — "reacomodar"
        /// podía aterrizar dos sesiones en la misma aula a la misma hora con EsFactible=true.
        /// Habría fallado antes del fix (EsFactible=true con ambas sesiones en Salón 1 el martes
        /// a las 08:00, en vez de reubicar una o reportar infactibilidad).
        /// </summary>
        [Fact]
        public async Task MoverSesionSinReespecificarEspacio_DetectaChoqueContraSuPropiaAulaActual()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();

            var bloqueLunes  = BloqueReal(DiaDeSemana.Lunes, 8);
            var bloqueMartes = BloqueReal(DiaDeSemana.Martes, 8);

            // A y B ya comparten aula (Salón 1) hoy, pero en días distintos — sin conflicto todavía.
            var sesionA = SesionSinAulaFija(_asigAId, _grupoAId, bloqueLunes.Id);
            var sesionB = SesionSinAulaFija(_asigBId, _grupoBId, bloqueMartes.Id);
            sesionRepo.Items.Add(sesionA);
            sesionRepo.Items.Add(sesionB);
            asigRepo.Items.Add(new AsignacionSemanal(Guid.NewGuid(), sesionA.Id, SemanaAcademica.A, bloqueLunes.Id, _salon1Id, Modalidad.Presencial));
            asigRepo.Items.Add(new AsignacionSemanal(Guid.NewGuid(), sesionB.Id, SemanaAcademica.A, bloqueMartes.Id, _salon1Id, Modalidad.Presencial));
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { sesionA.Id, sesionB.Id });
            horarioRepo.Items.Add(horario);

            var reacomodarSvc = CrearReacomodarServicio(horarioRepo, sesionRepo, asigRepo, uow);
            // Mover A al slot de B, sin especificar EspacioId: A se queda en Salón 1 (el que ya
            // tenía) — que es justo el aula que B ya ocupa ese día y hora.
            var resultado = await reacomodarSvc.EjecutarAsync(new ReacomodarHorarioRequest
            {
                HorarioId = horario.Id,
                SesionEditadaId = sesionA.Id,
                Dia = "martes",
                HoraInicio = "08:00"
            });

            Assert.True(resultado.EsFactible, resultado.MensajeError);
            var filasEnSalon1MartesA8 = resultado.Sesiones
                .Where(s => s.Dia == "martes" && s.HoraInicio == "08:00" && s.EspacioId == _salon1Id.ToString())
                .ToList();
            // Nunca dos sesiones presenciales en la misma aula, mismo día, misma hora.
            Assert.Single(filasEnSalon1MartesA8);
        }
    }
}
