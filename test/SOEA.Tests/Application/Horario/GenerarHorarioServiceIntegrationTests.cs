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
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(Items.ToList());
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

        /// <summary>Repo fake con los 4 criterios de sistema (MultiplesSesiones orden 1, Electiva orden 2,
        /// Optativa orden 3, Elegible orden 4, todos activos) — mismo estado que deja el seed real de la migración.</summary>
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
                IReadOnlyList<Guid>? sesionesCedidasParaRevertir = null,
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
            fase3 ??= new MotorGenetico(NullLogger<MotorGenetico>.Instance,
                new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance));
            return new GenerarHorarioService(fase1, fase2, fase3, horarioRepo, sesionRepo, asigRepo, new FakeGrupoRepo(), new FakeCriterioCesionRepo(), uow);
        }

        private static readonly string LabId = Guid.NewGuid().ToString();
        private static readonly string SalonId = Guid.NewGuid().ToString();
        private static readonly string QuimicaId = Guid.NewGuid().ToString();
        private static readonly string CalculoId = Guid.NewGuid().ToString();
        private static readonly string EticaId = Guid.NewGuid().ToString();

        // JSON crudo (mismo shape que produce la UI) con toda la semana restringida a Matutino/Vespertino.
        private const string DisponibilidadUiJsonMatutino =
            """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"martes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"miercoles":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"jueves":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"viernes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"sabado":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"}}""";

        private static GenerarHorarioRequest RequestBase() => new()
        {
            Semestre = "2026-1",
            Asignaturas = new List<AsignaturaDto>
            {
                new()
                {
                    Id = QuimicaId, Nombre = "Química Orgánica",
                    SesionesLaboratorioSemana = 1, HorasLaboratorio = 2,
                    Alternancia = "TipoA", Categoria = "Obligatoria"
                },
                new()
                {
                    Id = CalculoId, Nombre = "Cálculo I",
                    SesionesTeoriaPresencialSemana = 2, HorasTeoriaPresencial = 2,
                    Categoria = "Obligatoria",
                    // HC-VH: toda sesión de esta asignatura debe caer en [08:00, 12:00].
                    HoraInicioMin = "08:00", HoraFinMax = "12:00"
                },
                new()
                {
                    Id = EticaId, Nombre = "Ética",
                    SesionesTeoriaVirtualSemana = 1, HorasTeoriaVirtual = 2,
                    Categoria = "Electiva"
                }
            },
            Espacios = new List<EspacioDto>
            {
                new() { Id = LabId,   Nombre = "Lab Química", Tipo = "laboratorio", Capacidad = 30 },
                new() { Id = SalonId, Nombre = "Salón 101",   Tipo = "salon",       Capacidad = 30 }
            },
            // Multi-grupo real (P1): un grupo por asignatura — las sesiones se expanden desde el
            // grupo, no desde la asignatura. Los 3 comparten disponibilidad Matutino (HC-G01).
            Grupos = new List<GrupoDto>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Cohorte Química",
                    AsignaturaId = QuimicaId, EstudiantesInscritos = 20,
                    DisponibilidadUiJson = DisponibilidadUiJsonMatutino
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Cohorte Cálculo",
                    AsignaturaId = CalculoId, EstudiantesInscritos = 20,
                    DisponibilidadUiJson = DisponibilidadUiJsonMatutino
                },
                new()
                {
                    Id = Guid.NewGuid().ToString(), Nombre = "Cohorte Ética",
                    AsignaturaId = EticaId, EstudiantesInscritos = 20,
                    DisponibilidadUiJson = DisponibilidadUiJsonMatutino
                }
            }
        };

        private static int Hora(string hhmm) => int.Parse(hhmm.Split(':')[0]);

        /// <summary>Aserción post-hoc de HC-C01: dentro de cada semana, ninguna pareja de sesiones
        /// del MISMO grupo se solapa. Multi-grupo real (P1): grupos distintos son cohortes de
        /// estudiantes distintas y sí pueden solaparse; en este fixture cada grupo tiene exactamente
        /// una asignatura, así que agrupar por AsignaturaId identifica al grupo.</summary>
        private static void AssertSinSolapesDeCohorte(IEnumerable<SesionGeneradaDto> sesiones)
        {
            foreach (var grupoSemanaDia in sesiones.GroupBy(s => (s.Semana, s.Dia, s.AsignaturaId)))
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
            // 4 sesiones ⇒ 4 filas persistidas, más la contraparte virtual DERIVADA del lab TipoA
            // (la única que alterna) = 5 DTOs. Sin alternancia no hay segunda semana que dibujar.
            Assert.Equal(5, r.Sesiones.Count);

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

            // Regla 9 / ALT-05: el lab TipoA es presencial en A y su contraparte derivada es
            // virtual en B, en la misma franja.
            var labSesiones = r.Sesiones.Where(s => s.Alternancia == nameof(TipoAlternancia.TipoA)).ToList();
            Assert.Equal(2, labSesiones.Count);
            var labA = labSesiones.Single(s => s.Semana == "A");
            var labB = labSesiones.Single(s => s.Semana == "B");
            Assert.False(labA.Virtual);
            Assert.True(labB.Virtual);
            Assert.Equal(labA.HoraInicio, labB.HoraInicio);
            Assert.Equal(labA.Dia, labB.Dia);

            // Persistencia: 4 sesiones + 1 horario + 4 asignaciones (una por sesión), en una
            // transacción confirmada.
            Assert.Equal(4, sesionRepo.Items.Count);
            Assert.Single(horarioRepo.Items);
            Assert.Equal(4, asigRepo.Items.Count);
            Assert.Equal(1, uow.Commits);
            Assert.Equal(0, uow.Rollbacks);
            Assert.Equal(0, horarioRepo.Items[0].ViolacionesRestriccionesDuras);
        }

        /// <summary>
        /// P6 auditoría: antes no existía ningún GET para el horario ya persistido — el frontend
        /// solo tenía las sesiones en memoria hasta la próxima generación, así que un simple reload
        /// de /horario dejaba la grilla vacía aunque el horario siguiera intacto en BD. Confirma que
        /// ObtenerActualAsync reconstruye el mismo resultado que devolvió la generación original.
        /// </summary>
        [Fact]
        public async Task ObtenerActualAsync_TrasGenerar_DevuelveElMismoHorarioPersistido()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var request = RequestBase();
            var generado = await svc.EjecutarAsync(request);
            Assert.True(generado.EsFactible, generado.MensajeError ?? string.Join("\n", generado.Logs));

            var actual = await svc.ObtenerActualAsync(request.Semestre);

            Assert.NotNull(actual);
            Assert.True(actual!.EsFactible);
            Assert.Equal(generado.HorarioId, actual.HorarioId);
            Assert.Equal(generado.PuntajeFitness, actual.PuntajeFitness);
            Assert.Equal(generado.Sesiones.Count, actual.Sesiones.Count);
            Assert.Equal(
                generado.Sesiones.Select(s => s.Id).OrderBy(id => id),
                actual.Sesiones.Select(s => s.Id).OrderBy(id => id));
        }

        [Fact]
        public async Task ObtenerActualAsync_SinNingunaGeneracionPrevia_DevuelveNull()
        {
            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());

            var actual = await svc.ObtenerActualAsync("2026-1");

            Assert.Null(actual);
        }

        /// <summary>
        /// M2 (auditoría): antes, cada POST /horario/generar solo AGREGABA sesiones/asignaciones
        /// (AddRangeAsync sin delete previo) — las de la corrida anterior quedaban huérfanas en la
        /// BD para siempre (crecimiento ilimitado + contaminación de cualquier query que no
        /// filtrara por horario activo, el mismo problema que "G4 auditoría" documenta en
        /// AsignarDocenteSesionService). Verifica que regenerar limpia la corrida anterior, sin
        /// tocar sesiones manuales (que no pertenecen a ningún Horario.SesioneIds) ni los registros
        /// Horario en sí (auditoría de corridas — IHorarioRepositorio.GetAllAsync).
        /// </summary>
        [Fact]
        public async Task Regenerar_BorraSesionesYAsignacionesDeLaCorridaAnterior_PeroConservaSesionesManualesYHorarios()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var request = RequestBase();

            // Sesión manual (CrearSesionManualService): nunca entra en Horario.SesioneIds.
            var manual = new Sesion(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);
            sesionRepo.Items.Add(manual);

            var r1 = await svc.EjecutarAsync(request);
            Assert.True(r1.EsFactible, r1.MensajeError ?? string.Join("\n", r1.Logs));

            var idsPrimeraCorrida = sesionRepo.Items.Select(s => s.Id).Where(id => id != manual.Id).ToList();
            Assert.NotEmpty(idsPrimeraCorrida);
            var horarioPrimeraCorridaId = horarioRepo.Items.Single().Id;

            var r2 = await svc.EjecutarAsync(request);
            Assert.True(r2.EsFactible, r2.MensajeError ?? string.Join("\n", r2.Logs));

            Assert.DoesNotContain(sesionRepo.Items, s => idsPrimeraCorrida.Contains(s.Id));
            Assert.DoesNotContain(asigRepo.Items, a => idsPrimeraCorrida.Contains(a.SesionId));
            Assert.Contains(sesionRepo.Items, s => s.Id == manual.Id);
            Assert.Contains(horarioRepo.Items, h => h.Id == horarioPrimeraCorridaId);
            Assert.Equal(2, horarioRepo.Items.Count);
        }

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.2): la limpieza de "corridas anteriores"
        /// no filtraba por semestre — regenerar el horario de "2026-2" borraba también las
        /// sesiones vivas de "2026-1", aunque nadie las hubiera tocado, dejando el Horario de ese
        /// semestre con SesioneIds colgando (ObtenerActualAsync pasaba a devolver null para él).
        /// Habría fallado antes del fix: idsSemestre1 quedaba vacío tras generar "2026-2".
        /// </summary>
        [Fact]
        public async Task Regenerar_OtroSemestre_NoBorraLasSesionesDeUnSemestreDistinto()
        {
            var horarioRepo = new FakeHorarioRepo();
            var sesionRepo  = new FakeSesionRepo();
            var asigRepo    = new FakeAsignacionRepo();
            var uow         = new FakeUow();
            var svc = CrearServicio(horarioRepo, sesionRepo, asigRepo, uow);

            var requestSemestre1 = RequestBase();
            requestSemestre1.Semestre = "2026-1";
            var r1 = await svc.EjecutarAsync(requestSemestre1);
            Assert.True(r1.EsFactible, r1.MensajeError ?? string.Join("\n", r1.Logs));

            var idsSemestre1 = sesionRepo.Items.Select(s => s.Id).ToList();
            Assert.NotEmpty(idsSemestre1);

            var requestSemestre2 = RequestBase();
            requestSemestre2.Semestre = "2026-2";
            var r2 = await svc.EjecutarAsync(requestSemestre2);
            Assert.True(r2.EsFactible, r2.MensajeError ?? string.Join("\n", r2.Logs));

            // Las sesiones de 2026-1 deben seguir todas ahí — regenerar 2026-2 no las tocó.
            Assert.All(idsSemestre1, id => Assert.Contains(sesionRepo.Items, s => s.Id == id));
            Assert.All(idsSemestre1, id => Assert.Contains(asigRepo.Items, a => a.SesionId == id));

            // Y GET /horario/actual?semestre=2026-1 sigue respondiendo con su horario intacto.
            var actualSemestre1 = await svc.ObtenerActualAsync("2026-1");
            Assert.NotNull(actualSemestre1);
            Assert.True(actualSemestre1!.EsFactible);
            Assert.NotEmpty(actualSemestre1.Sesiones);

            Assert.Equal(2, horarioRepo.Items.Count);
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

        // ── RequisitosEspacio de grupo, de punta a punta (M9: RequestBase() nunca los poblaba,
        // así que ningún test de integración probaba el camino completo DTO → dominio → motores).

        private static (string asigId, string grupoId, GenerarHorarioRequest request) RequestMinimaTeoriaPresencial(
            List<EspacioDto> espacios, List<RequisitoEspacioDto> requisitosEspacio)
        {
            var asigId = Guid.NewGuid().ToString();
            var grupoId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new()
                    {
                        Id = asigId, Nombre = "Asignatura de prueba",
                        SesionesTeoriaPresencialSemana = 1, HorasTeoriaPresencial = 2,
                        Categoria = "Obligatoria"
                    }
                },
                Espacios = espacios,
                Grupos = new List<GrupoDto>
                {
                    new()
                    {
                        Id = grupoId, Nombre = "Grupo de prueba",
                        AsignaturaId = asigId, EstudiantesInscritos = 20,
                        DisponibilidadUiJson = DisponibilidadUiJsonMatutino,
                        RequisitosEspacio = requisitosEspacio
                    }
                }
            };
            return (asigId, grupoId, request);
        }

        [Fact]
        public async Task RequisitoDeAulaConcreta_SeRespeta()
        {
            var salonPreferido = Guid.NewGuid().ToString();
            var salonOtro = Guid.NewGuid().ToString();
            var (_, _, request) = RequestMinimaTeoriaPresencial(
                espacios: new List<EspacioDto>
                {
                    new() { Id = salonPreferido, Nombre = "Salón preferido", Tipo = "salon", Capacidad = 30 },
                    new() { Id = salonOtro, Nombre = "Salón otro", Tipo = "salon", Capacidad = 30 }
                },
                requisitosEspacio: new List<RequisitoEspacioDto>
                {
                    new() { TipoSesion = "TeoriaPresencial", EspacioId = salonPreferido, TipoEspacio = "Salon", Sesiones = 1 }
                });

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.NotEmpty(r.Sesiones);
            Assert.All(r.Sesiones, s => Assert.Equal(salonPreferido, s.EspacioId));
        }

        // M8: EspaciosController usa "Salón" con tilde como literal canónico del tipo de espacio
        // (distinto del "Salon" sin tilde que usa RequisitoEspacioDto); un espacio real con ese
        // tipo NO debe generar una advertencia — sólo lo genuinamente no reconocido debe hacerlo.
        [Fact]
        public async Task EspacioConTipoSalonConTilde_NoGeneraAdvertencia()
        {
            var (_, _, request) = RequestMinimaTeoriaPresencial(
                espacios: new List<EspacioDto>
                {
                    new() { Id = Guid.NewGuid().ToString(), Nombre = "Salón 101", Tipo = "Salón", Capacidad = 30 }
                },
                requisitosEspacio: new List<RequisitoEspacioDto>());

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.DoesNotContain(r.Logs, l => l.Contains("no reconocido"));
        }

        [Fact]
        public async Task EspacioConTipoNoReconocido_GeneraAdvertenciaYUsaSalonPorDefecto()
        {
            var (_, _, request) = RequestMinimaTeoriaPresencial(
                espacios: new List<EspacioDto>
                {
                    new() { Id = Guid.NewGuid().ToString(), Nombre = "Aula rara", Tipo = "AulaXYZ", Capacidad = 30 }
                },
                requisitosEspacio: new List<RequisitoEspacioDto>());

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.Contains(r.Logs, l => l.Contains("[WARN]") && l.Contains("Aula rara") && l.Contains("AulaXYZ"));
        }

        [Fact]
        public async Task RequisitoSoloLaboratorio_ExcluyeSalones()
        {
            var lab = Guid.NewGuid().ToString();
            var salon = Guid.NewGuid().ToString();
            // Sesión de teoría presencial: la regla por defecto EXCLUYE laboratorio (petición 7).
            // El requisito del grupo la reautoriza explícitamente para un laboratorio.
            var (_, _, request) = RequestMinimaTeoriaPresencial(
                espacios: new List<EspacioDto>
                {
                    new() { Id = lab, Nombre = "Laboratorio", Tipo = "laboratorio", Capacidad = 30 },
                    new() { Id = salon, Nombre = "Salón", Tipo = "salon", Capacidad = 30 }
                },
                requisitosEspacio: new List<RequisitoEspacioDto>
                {
                    new() { TipoSesion = "TeoriaPresencial", TipoEspacio = "Laboratorio", Sesiones = 1 }
                });

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.NotEmpty(r.Sesiones);
            Assert.All(r.Sesiones, s => Assert.Equal(lab, s.EspacioId));
        }

        [Fact]
        public async Task RequisitoImposible_RetornaInfactibleConMensajeDeEspacio()
        {
            // El requisito del grupo exige un espacio concreto que no existe entre los espacios
            // del run (p. ej. borrado del catálogo tras configurar el requisito). M10: a diferencia
            // de un Sesion.EspacioId ausente, esto NO cae al filtro genérico por tipo — la sesión
            // queda sin ningún candidato y el pipeline debe fallar de forma explícita, nunca
            // devolver un horario que ignore el requisito en silencio.
            var espacioInexistente = Guid.NewGuid().ToString();
            var salonReal = Guid.NewGuid().ToString();
            var (_, _, request) = RequestMinimaTeoriaPresencial(
                espacios: new List<EspacioDto>
                {
                    new() { Id = salonReal, Nombre = "Salón real", Tipo = "salon", Capacidad = 30 }
                },
                requisitosEspacio: new List<RequisitoEspacioDto>
                {
                    new() { TipoSesion = "TeoriaPresencial", EspacioId = espacioInexistente, TipoEspacio = "Salon", Sesiones = 1 }
                });

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.False(r.EsFactible);
            Assert.False(string.IsNullOrEmpty(r.MensajeError));
            Assert.Contains("espacio", r.MensajeError, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(r.Sesiones);
        }

        [Fact]
        public async Task RequisitoDeGrupoConTipoEspacioAusente_CaeALaReglaPorDefecto_NoASalon()
        {
            // M6, de punta a punta: una entrada de requisito con EspacioId y TipoEspacio ambos
            // ausentes del JSON (p. ej. sólo declara Sesiones). Antes del fix, RequisitoEspacio
            // .TipoEspacio no era nullable y el parseo de "ausente" caía a Salon — una sesión de
            // LABORATORIO con ese requisito quedaba sin ningún candidato en un run que sólo tiene
            // un laboratorio, aunque el usuario nunca pidió "sólo salón". Con el fix, cae a la
            // regla por defecto (Laboratorio exige Laboratorio) y encuentra el candidato correcto.
            var labId = Guid.NewGuid().ToString();
            var asigId = Guid.NewGuid().ToString();
            var grupoId = Guid.NewGuid().ToString();
            var request = new GenerarHorarioRequest
            {
                Semestre = "2026-1",
                Asignaturas = new List<AsignaturaDto>
                {
                    new()
                    {
                        Id = asigId, Nombre = "Laboratorio de prueba",
                        SesionesLaboratorioSemana = 1, HorasLaboratorio = 2,
                        Alternancia = "SinAlternancia", Categoria = "Obligatoria"
                    }
                },
                Espacios = new List<EspacioDto>
                {
                    new() { Id = labId, Nombre = "Único laboratorio", Tipo = "laboratorio", Capacidad = 30 }
                },
                Grupos = new List<GrupoDto>
                {
                    new()
                    {
                        Id = grupoId, Nombre = "Grupo de prueba",
                        AsignaturaId = asigId, EstudiantesInscritos = 20,
                        DisponibilidadUiJson = DisponibilidadUiJsonMatutino,
                        RequisitosEspacio = new List<RequisitoEspacioDto>
                        {
                            new() { TipoSesion = "Laboratorio", EspacioId = null, TipoEspacio = null, Sesiones = 1 }
                        }
                    }
                }
            };

            var svc = CrearServicio(new FakeHorarioRepo(), new FakeSesionRepo(), new FakeAsignacionRepo(), new FakeUow());
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.NotEmpty(r.Sesiones);
            Assert.All(r.Sesiones.Where(s => !s.Virtual), s => Assert.Equal(labId, s.EspacioId));
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
