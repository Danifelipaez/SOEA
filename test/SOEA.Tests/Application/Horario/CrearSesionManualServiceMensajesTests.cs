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
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Goal de sesión: los 409/infactibilidades inducidas por el usuario al crear una sesión a
    /// mano (HC-I01 docente ocupado, HC-S01 espacio ocupado) deben identificar la sesión con la
    /// que chocan (no solo "el docente ya tiene otra sesión"), en español y con sugerencia.
    /// </summary>
    public class CrearSesionManualServiceMensajesTests
    {
        // MAN1 auditoría: id determinístico de GrillaInstitucional, no Guid.NewGuid() — el servicio
        // indexa contra la grilla canónica para detectar solapes por span, y necesita que los ids
        // de esta sesión existente coincidan con los que resuelve _bloques.FindByDiaHoraAsync.
        private static BloqueTiempo Bloque(DiaDeSemana dia, int hora) =>
            SOEA.Domain.Services.GrillaInstitucional.GenerarBloques()
                .First(b => b.Dia == dia && b.HoraInicio == new TimeOnly(hora, 0));

        [Fact]
        public async Task HCI01_DocenteYaOcupado_MensajeIdentificaAmbasAsignaturasConDiaYHora()
        {
            var bloque = Bloque(DiaDeSemana.Lunes, 7);
            var docenteId = Guid.NewGuid();
            var asigExistenteId = Guid.NewGuid();
            var asigNuevaId = Guid.NewGuid();

            var sesionExistente = new Sesion(Guid.NewGuid(), asigExistenteId, docenteId, bloque.Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);

            var svc = new CrearSesionManualService(
                new FakeBloques(bloque),
                new FakeSesiones(sesionExistente),
                new FakeAsignaciones(),
                new FakeUnitOfWork(),
                new FakeAsignaturas(
                    new Asignatura(asigExistenteId, "Física I", "COD-FIS", 1, 1, 0, Guid.NewGuid()),
                    new Asignatura(asigNuevaId, "Cálculo I", "COD-CALC", 1, 1, 0, Guid.NewGuid())));

            var req = new CrearSesionManualRequest
            {
                AsignaturaId = asigNuevaId,
                DocenteId = docenteId,
                Dia = "lunes",
                HoraInicio = "07:00",
                DuracionHoras = 1m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            };

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => svc.EjecutarAsync(req));

            Assert.Contains("HC-I01", ex.Message);
            Assert.Contains("Sesión 1", ex.Message);
            Assert.Contains("Sesión 2", ex.Message);
            Assert.Contains("Cálculo I", ex.Message);
            Assert.Contains("Física I", ex.Message);
            Assert.Contains("lunes", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("07:00", ex.Message);
            Assert.Contains("Elija una hora diferente", ex.Message);
        }

        [Fact]
        public async Task HCS01_EspacioYaOcupado_MensajeIdentificaAmbasAsignaturas()
        {
            var bloque = Bloque(DiaDeSemana.Martes, 8);
            var espacioId = Guid.NewGuid();
            var asigExistenteId = Guid.NewGuid();
            var asigNuevaId = Guid.NewGuid();

            var sesionExistente = new Sesion(Guid.NewGuid(), asigExistenteId, Guid.NewGuid(), bloque.Id, espacioId, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false);
            var asigExistente = new AsignacionSemanal(Guid.NewGuid(), sesionExistente.Id, SemanaAcademica.A, bloque.Id, espacioId, Modalidad.Presencial);

            var svc = new CrearSesionManualService(
                new FakeBloques(bloque),
                new FakeSesiones(sesionExistente),
                new FakeAsignaciones(asigExistente),
                new FakeUnitOfWork(),
                new FakeAsignaturas(
                    new Asignatura(asigExistenteId, "Física I", "COD-FIS", 1, 1, 0, Guid.NewGuid()),
                    new Asignatura(asigNuevaId, "Cálculo I", "COD-CALC", 1, 1, 0, Guid.NewGuid())));

            var req = new CrearSesionManualRequest
            {
                AsignaturaId = asigNuevaId,
                DocenteId = Guid.NewGuid(),
                EspacioId = espacioId,
                Dia = "martes",
                HoraInicio = "08:00",
                DuracionHoras = 1m,
                TipoFlujo = "Laboratorio"
            };

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => svc.EjecutarAsync(req));

            Assert.Contains("HC-S01", ex.Message);
            Assert.Contains("Sesión 1", ex.Message);
            Assert.Contains("Sesión 2", ex.Message);
            Assert.Contains("Cálculo I", ex.Message);
            Assert.Contains("Física I", ex.Message);
            Assert.Contains("Elija otro espacio", ex.Message);
        }

        // ── Fakes ────────────────────────────────────────────────────────────────

        private sealed class FakeBloques : IBloqueTiempoRepositorio
        {
            private readonly Dictionary<Guid, BloqueTiempo> _store = new();
            public FakeBloques(params BloqueTiempo[] bloques) { foreach (var b in bloques) _store[b.Id] = b; }
            public Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio) =>
                Task.FromResult(_store.Values.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio));
            public Task<bool> ExisteAlgunoAsync() => Task.FromResult(_store.Count > 0);
            public Task AddAsync(BloqueTiempo entity) { _store[entity.Id] = entity; return Task.CompletedTask; }
            public Task<BloqueTiempo?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<BloqueTiempo>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(BloqueTiempo entity) { _store[entity.Id] = entity; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        private sealed class FakeSesiones : ISesionRepositorio
        {
            private readonly List<Sesion> _store = new();
            public FakeSesiones(params Sesion[] sesiones) => _store.AddRange(sesiones);
            public Task AddAsync(Sesion e) { _store.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) { _store.AddRange(sesiones); return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId) => Task.FromResult(false);
            public Task<Sesion?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(s => s.Id == id));
            public Task<List<Sesion>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task UpdateAsync(Sesion e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task DeleteRangeAsync(IEnumerable<Guid> ids) => Task.CompletedTask;
            public Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids) { var set = ids.ToHashSet(); return Task.FromResult(_store.Where(s => set.Contains(s.Id)).ToList()); }
        }

        private sealed class FakeAsignaciones : IAsignacionSemanalRepositorio
        {
            private readonly List<AsignacionSemanal> _store = new();
            public FakeAsignaciones(params AsignacionSemanal[] asigs) => _store.AddRange(asigs);
            public Task AddAsync(AsignacionSemanal e) { _store.Add(e); return Task.CompletedTask; }
            public Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones) { _store.AddRange(asignaciones); return Task.CompletedTask; }
            public Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds) =>
                Task.FromResult(_store.Where(a => sesionIds.Contains(a.SesionId)).ToList());
            public Task<AsignacionSemanal?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(a => a.Id == id));
            public Task<List<AsignacionSemanal>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task UpdateAsync(AsignacionSemanal e) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task DeleteBySesionIdsAsync(IEnumerable<Guid> sesionIds) => Task.CompletedTask;
        }

        private sealed class FakeAsignaturas : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();
            public FakeAsignaturas(params Asignatura[] asignaturas) { foreach (var a in asignaturas) _store[a.Id] = a; }
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
