using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Exceptions;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Goal de sesión: los errores de infactibilidad inducidos por el usuario deben ser claros,
    /// en español, con sugerencia según el tipo de error, y referenciar las sesiones en conflicto
    /// por nombre de grupo/asignatura ("Sesión 1"/"Sesión 2"), día y hora. Antes, el 409 de
    /// HC-I01 (edición) solo decía "el docente ya tiene otra sesión que se solapa" sin identificar
    /// cuál — un coordinador no podía saber qué mover.
    /// </summary>
    public class AsignarDocenteSesionServiceMensajesTests
    {
        private static BloqueTiempo Bloque(DiaDeSemana dia, int hora) =>
            new(Guid.NewGuid(), dia, new TimeOnly(hora, 0), new TimeOnly(hora + 1, 0));

        [Fact]
        public async Task Asignar_SolapeEnFranja_MensajeIdentificaAmbasSesionesConNombreDiaYHora()
        {
            var bloqueA = Bloque(DiaDeSemana.Lunes, 7); // 07:00-08:00
            var bloqueB = Bloque(DiaDeSemana.Lunes, 8); // 08:00-09:00
            var docente = new Docente(Guid.NewGuid(), "Doc", "", "doc@soea.edu", 40m,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });

            var asigTargetId = Guid.NewGuid();
            var asigExistenteId = Guid.NewGuid();
            var grupoId = Guid.NewGuid();

            // sesionTarget: 07:00-09:00 (span de 2h desde bloqueA); sesionExistente: 08:00-09:00 (1h desde bloqueB) → solapan.
            var sesionTarget = new Sesion(Guid.NewGuid(), asigTargetId, null, bloqueA.Id, null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 2m, false, false);
            var sesionExistente = new Sesion(Guid.NewGuid(), asigExistenteId, docente.Id, bloqueB.Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);

            var asigTarget = new AsignacionSemanal(Guid.NewGuid(), sesionTarget.Id, SemanaAcademica.A, bloqueA.Id, null, Modalidad.Virtual);
            var asigExistente = new AsignacionSemanal(Guid.NewGuid(), sesionExistente.Id, SemanaAcademica.A, bloqueB.Id, null, Modalidad.Virtual);

            var grupo = new Grupo(grupoId, "G1", Guid.NewGuid(), 1);
            var asignaturaTarget = new Asignatura(asigTargetId, "Cálculo I", "COD-CALC", 2, 1, 0, Guid.NewGuid());
            var asignaturaExistente = new Asignatura(asigExistenteId, "Física I", "COD-FIS", 1, 1, 0, Guid.NewGuid());

            var svc = new AsignarDocenteSesionService(
                new FakeSesionRepo(sesionTarget, sesionExistente),
                new FakeAsignacionRepo(asigTarget, asigExistente),
                new FakeBloqueRepo(bloqueA, bloqueB),
                new FakeDocenteRepo(docente),
                grupos: new FakeGrupoRepo(grupo),
                asignaturas: new FakeAsignaturaRepo(asignaturaTarget, asignaturaExistente));

            var req = new AsignarDocenteRequest { SesionId = sesionTarget.Id, DocenteId = docente.Id };

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => svc.EjecutarAsync(req));

            Assert.Contains("HC-I01", ex.Message);
            Assert.Contains("Sesión 1", ex.Message);
            Assert.Contains("Sesión 2", ex.Message);
            Assert.Contains("Cálculo I · G1", ex.Message); // sesión con grupo → asignatura · grupo
            Assert.Contains("Física I", ex.Message);        // sesión sin grupo → solo asignatura
            Assert.Contains("lunes", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("07:00", ex.Message);
            Assert.Contains("08:00", ex.Message);
            // Sugerencia adaptada al tipo de error (HC-I01: cambiar docente u horario).
            Assert.Contains("Elija otro docente", ex.Message);
        }

        // ── Fakes en memoria ─────────────────────────────────────────────────────

        private sealed class FakeSesionRepo : ISesionRepositorio
        {
            private readonly Dictionary<Guid, Sesion> _store = new();
            public FakeSesionRepo(params Sesion[] sesiones) { foreach (var s in sesiones) _store[s.Id] = s; }
            public Task AddAsync(Sesion e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(Sesion e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task DeleteRangeAsync(IEnumerable<Guid> ids) { foreach (var id in ids) _store.Remove(id); return Task.CompletedTask; }
            public Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids) { var set = ids.ToHashSet(); return Task.FromResult(_store.Values.Where(s => set.Contains(s.Id)).ToList()); }
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) { foreach (var s in sesiones) _store[s.Id] = s; return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId) =>
                Task.FromResult(_store.Values.Any(s => s.AsignaturaId == asignaturaId && s.DocenteId == docenteId && s.BloqueTiempoId == bloqueTiempoId));
        }

        private sealed class FakeAsignacionRepo : IAsignacionSemanalRepositorio
        {
            private readonly List<AsignacionSemanal> _store = new();
            public FakeAsignacionRepo(params AsignacionSemanal[] asigs) => _store.AddRange(asigs);
            public Task AddAsync(AsignacionSemanal e) { _store.Add(e); return Task.CompletedTask; }
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task UpdateAsync(AsignacionSemanal e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { _store.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
            public Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds) { var set = sesionIds.ToHashSet(); _store.RemoveAll(a => set.Contains(a.SesionId)); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asigs) { _store.AddRange(asigs); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> ids)
            {
                var set = ids.ToHashSet();
                return Task.FromResult(_store.Where(a => set.Contains(a.SesionId)).ToList());
            }
        }

        private sealed class FakeBloqueRepo : IBloqueTiempoRepositorio
        {
            private readonly Dictionary<Guid, BloqueTiempo> _store = new();
            public FakeBloqueRepo(params BloqueTiempo[] bloques) { foreach (var b in bloques) _store[b.Id] = b; }
            public Task AddAsync(BloqueTiempo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<BloqueTiempo?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<BloqueTiempo>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(BloqueTiempo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio) =>
                Task.FromResult(_store.Values.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio));
            public Task<bool> ExisteAlgunoAsync() => Task.FromResult(_store.Count > 0);
        }

        private sealed class FakeDocenteRepo : IDocenteRepositorio
        {
            private readonly Dictionary<Guid, Docente> _store = new();
            public FakeDocenteRepo(params Docente[] docentes) { foreach (var d in docentes) _store[d.Id] = d; }
            public Task AddAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<Docente?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Docente>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Docente?> GetByCedulaAsync(string cedula) =>
                Task.FromResult(_store.Values.FirstOrDefault(d => d.CedulaIdentidad == cedula));
            public Task<Docente?> GetByNombreAsync(string nombre) =>
                Task.FromResult(_store.Values.FirstOrDefault(d =>
                    d.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)));
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            private readonly Dictionary<Guid, Grupo> _store = new();
            public FakeGrupoRepo(params Grupo[] grupos) { foreach (var g in grupos) _store[g.Id] = g; }
            public Task AddAsync(Grupo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(Grupo e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Grupo?>(null);
            public Task<Grupo?> GetByCodigoAsync(string codigo) => Task.FromResult<Grupo?>(null);
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) => Task.FromResult(Enumerable.Empty<Grupo>());
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) => Task.FromResult(Enumerable.Empty<Grupo>());
        }

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();
            public FakeAsignaturaRepo(params Asignatura[] asignaturas) { foreach (var a in asignaturas) _store[a.Id] = a; }
            public Task AddAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<Asignatura?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Asignatura?> GetByCodigoAsync(string codigo) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) => Task.FromResult<Asignatura?>(null);
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Asignatura?>(null);
        }
    }
}
