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
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// La regla de negocio central: la Semana A es EL horario, y la Semana B solo aparece cuando,
    /// tras comprobar que no existe configuración válida por falta de aulas, dos sesiones se
    /// emparejan para turnarse el aula. Pipeline real (CP-SAT + GA), sin motores falsos.
    /// </summary>
    public class SemanaBActivacionIntegrationTests
    {
        // ── Fakes mínimos (convención del proyecto: escritos a mano, sin librería de mocks) ──
        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            public readonly List<SOEA.Domain.Entities.Horario> Items = new();
            public Task AddAsync(SOEA.Domain.Entities.Horario e) { Items.Add(e); return Task.CompletedTask; }
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(h => h.Id == id));
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(SOEA.Domain.Entities.Horario e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(h => h.Id == id); return Task.CompletedTask; }
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) =>
                Task.FromResult(Items.LastOrDefault(h => h.Semestre == semestre));
        }

        private sealed class FakeSesionRepo : ISesionRepositorio
        {
            public readonly List<Sesion> Items = new();
            public Task AddAsync(Sesion e) { Items.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<Sesion> s) { Items.AddRange(s); return Task.CompletedTask; }
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(Sesion e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid a, Guid d, Guid b) => Task.FromResult(false);
        }

        private sealed class FakeAsignacionRepo : IAsignacionSemanalRepositorio
        {
            public readonly List<AsignacionSemanal> Items = new();
            public Task AddAsync(AsignacionSemanal e) { Items.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> a) { Items.AddRange(a); return Task.CompletedTask; }
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(Items.ToList());
            public Task UpdateAsync(AsignacionSemanal e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { Items.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> ids)
            {
                var set = ids.ToHashSet();
                return Task.FromResult(Items.Where(a => set.Contains(a.SesionId)).ToList());
            }
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            public Task AddAsync(Grupo e) => Task.CompletedTask;
            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult<Grupo?>(null);
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(new List<Grupo>());
            public Task UpdateAsync(Grupo e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Grupo?> GetByNombreYProgramaAsync(string n, Guid p) => Task.FromResult<Grupo?>(null);
            public Task<Grupo?> GetByCodigoAsync(string c) => Task.FromResult<Grupo?>(null);
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid a) => Task.FromResult(Enumerable.Empty<Grupo>());
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid d) => Task.FromResult(Enumerable.Empty<Grupo>());
        }

        private sealed class FakeCriterioCesionRepo : ICriterioCesionAlternanciaRepositorio
        {
            private readonly List<CriterioCesionAlternancia> _items = new()
            {
                new(CriterioCesionAlternancia.IdMultiplesSesiones, CriterioElegibilidadAlternancia.MultiplesSesiones, 1),
                new(CriterioCesionAlternancia.IdElectiva, CriterioElegibilidadAlternancia.Electiva, 2),
                new(CriterioCesionAlternancia.IdOptativa, CriterioElegibilidadAlternancia.Optativa, 3),
                new(CriterioCesionAlternancia.IdElegible, CriterioElegibilidadAlternancia.Elegible, 4)
            };
            public Task AddAsync(CriterioCesionAlternancia e) { _items.Add(e); return Task.CompletedTask; }
            public Task<CriterioCesionAlternancia?> GetByIdAsync(Guid id) => Task.FromResult(_items.FirstOrDefault(c => c.Id == id));
            public Task<List<CriterioCesionAlternancia>> GetAllAsync() => Task.FromResult(_items.ToList());
            public Task UpdateAsync(CriterioCesionAlternancia e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { _items.RemoveAll(c => c.Id == id); return Task.CompletedTask; }
        }

        private sealed class FakeUow : IUnitOfWork
        {
            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task CommitAsync() => Task.CompletedTask;
            public Task RollbackAsync() => Task.CompletedTask;
            public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
            public Task SaveAsync() => Task.CompletedTask;
            public void Track<T>(T entity) where T : EntidadBase { }
            public void Dispose() { }
        }

        private static (GenerarHorarioService svc, FakeAsignacionRepo asigs, FakeSesionRepo sesiones) Servicio()
        {
            var asigs = new FakeAsignacionRepo();
            var sesiones = new FakeSesionRepo();
            var svc = new GenerarHorarioService(
                new AgendadorColoracionGrafo(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance),
                new MotorConstraintProgramming(NullLogger<MotorConstraintProgramming>.Instance,
                    new CpSatOptions { NumWorkers = 1, TimeoutSegundos = 30 }),
                new MotorGenetico(NullLogger<MotorGenetico>.Instance,
                    new AsignadorEspaciosExactoCpSat(NullLogger<AsignadorEspaciosExactoCpSat>.Instance)),
                new FakeHorarioRepo(), sesiones, asigs, new FakeGrupoRepo(),
                new FakeCriterioCesionRepo(), new FakeUow());
            return (svc, asigs, sesiones);
        }

        // Disponibilidad: solo el lunes por la mañana. Con la ventana [08:00,10:00] de la asignatura
        // queda UN único inicio válido, que es lo que fuerza la competencia por el aula.
        private const string SoloLunesMatutino =
            """{"lunes":{"noDisponible":false,"tipo":"Franja general","franjaGeneral":"Matutino (06:00–12:00)"},"martes":{"noDisponible":true},"miercoles":{"noDisponible":true},"jueves":{"noDisponible":true},"viernes":{"noDisponible":true},"sabado":{"noDisponible":true}}""";

        private static GenerarHorarioRequest DosLabsUnSoloLaboratorio(string labId, params string[] asigIds) => new()
        {
            Semestre = "2026-1",
            Asignaturas = asigIds.Select((id, i) => new AsignaturaDto
            {
                Id = id,
                Nombre = i == 0 ? "Bioquímica" : "Química Orgánica",
                SesionesLaboratorioSemana = 1,
                HorasLaboratorio = 2,
                Categoria = "Electiva",           // elegible por el criterio de cesión "Electiva"
                HoraInicioMin = "08:00",
                HoraFinMax = "10:00"
            }).ToList(),
            Espacios = new List<EspacioDto>
            {
                new() { Id = labId, Nombre = "BIO", Tipo = "laboratorio", Capacidad = 30 }
            },
            Grupos = asigIds.Select((id, i) => new GrupoDto
            {
                Id = Guid.NewGuid().ToString(),
                Nombre = i == 0 ? "Grupo 1" : "Grupo 15",
                AsignaturaId = id,
                EstudiantesInscritos = 20,
                DisponibilidadUiJson = SoloLunesMatutino
            }).ToList()
        };

        [Fact]
        public async Task SinSaturacion_NoSeActivaLaSemanaB()
        {
            // Holgura de sobra: 2 laboratorios para 2 sesiones. Nada debe alternar, y la respuesta
            // no debe traer ninguna fila de semana B — es el caso que en producción devolvía un
            // horario duplicado y desalineado.
            var labA = Guid.NewGuid().ToString();
            var labB = Guid.NewGuid().ToString();
            var request = DosLabsUnSoloLaboratorio(labA, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
            request.Espacios.Add(new EspacioDto { Id = labB, Nombre = "BIO 2", Tipo = "laboratorio", Capacidad = 30 });

            var (svc, asigs, sesiones) = Servicio();
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));
            Assert.All(sesiones.Items, s => Assert.Equal(TipoAlternancia.SinAlternancia, s.Alternancia));
            Assert.All(sesiones.Items, s => Assert.Null(s.ParejaAlternanciaId));

            // Una fila persistida por sesión, y ningún DTO de semana B.
            Assert.Equal(sesiones.Items.Count, asigs.Items.Count);
            Assert.Equal(sesiones.Items.Count, r.Sesiones.Count);
            Assert.All(r.Sesiones, s => Assert.Equal(string.Empty, s.Semana));
            Assert.DoesNotContain(r.Sesiones, s => s.EsContraparteVirtual);
        }

        [Fact]
        public async Task SaturacionDeAulas_ActivaLaSemanaB_ConUnaParejaQueCompartéBloqueYAula()
        {
            // Un solo laboratorio y un solo inicio válido (lunes 08:00) para dos asignaturas de
            // grupos distintos: en una sola semana no caben. La única salida es emparejarlas —
            // el caso Bioquímica / Química Orgánica.
            var lab = Guid.NewGuid().ToString();
            var request = DosLabsUnSoloLaboratorio(lab, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

            var (svc, asigs, sesiones) = Servicio();
            var r = await svc.EjecutarAsync(request);

            Assert.True(r.EsFactible, r.MensajeError ?? string.Join("\n", r.Logs));

            // Exactamente una pareja, con tipos opuestos.
            var pareadas = sesiones.Items.Where(s => s.ParejaAlternanciaId.HasValue).ToList();
            Assert.Equal(2, pareadas.Count);
            Assert.Single(pareadas.Select(s => s.ParejaAlternanciaId!.Value).Distinct());
            Assert.Contains(pareadas, s => s.Alternancia == TipoAlternancia.TipoA);
            Assert.Contains(pareadas, s => s.Alternancia == TipoAlternancia.TipoB);

            // Nada se virtualizó en silencio para evitar el problema.
            Assert.All(sesiones.Items, s => Assert.Equal(Modalidad.Presencial, s.Modalidad));

            // Comparten bloque y aula, en semanas opuestas: una fila presencial cada una.
            var filas = pareadas.Select(s => asigs.Items.Single(a => a.SesionId == s.Id)).ToList();
            Assert.Equal(filas[0].BloqueTiempoId, filas[1].BloqueTiempoId);
            Assert.Equal(filas[0].EspacioId, filas[1].EspacioId);
            Assert.Equal(lab, filas[0].EspacioId!.Value.ToString());
            Assert.NotEqual(filas[0].Semana, filas[1].Semana);

            // La grilla recibe la presencial de cada semana MÁS la contraparte virtual derivada:
            // es el sub-cuadro que se dibuja dentro de la celda del aula.
            Assert.Equal(4, r.Sesiones.Count);
            Assert.Equal(2, r.Sesiones.Count(s => s.EsContraparteVirtual));
            Assert.All(r.Sesiones.Where(s => s.EsContraparteVirtual), s =>
            {
                Assert.True(s.Virtual);
                Assert.Null(s.EspacioId);
                Assert.Equal(lab, s.EspacioIdHogar);   // apunta al aula donde su pareja es presencial
            });
            Assert.All(r.Sesiones, s => Assert.NotEqual(string.Empty, s.ParejaId));
        }
    }
}
