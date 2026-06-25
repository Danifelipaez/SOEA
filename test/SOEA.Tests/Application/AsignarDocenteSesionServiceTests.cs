using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Verifica la lógica de asignación de docente post-generación (HU-04, Etapa 4).
    /// HC-I01 (solape de franja) es hard → lanza InvalidOperationException.
    /// HC-I02 (disponibilidad) y HC-I03 (carga máx.) son soft → solo advertencias.
    /// Presencial-first (CR-02/CR-08): el docente no participa en la generación del horario.
    /// </summary>
    public class AsignarDocenteSesionServiceTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static BloqueTiempo Bloque(DiaDeSemana dia, int hora) =>
            new(Guid.NewGuid(), dia, new TimeOnly(hora, 0), new TimeOnly(hora + 1, 0));

        private static Sesion CrearSesion(Guid bloqueId, decimal duracion = 2m, Guid? docenteId = null)
        {
            var s = new Sesion(
                Guid.NewGuid(), Guid.NewGuid(), docenteId, bloqueId, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, duracion, false, false);
            return s;
        }

        private static Docente CrearDocente(decimal maxHoras = 40m, BloqueTiempo? disponible = null)
        {
            var id = Guid.NewGuid();
            var d  = new Docente(id, "Doc", "", $"doc-{id}@soea.edu", maxHoras,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
            if (disponible is not null)
                d.AgregarBloqueDisponibilidad(disponible);
            return d;
        }

        private static AsignacionSemanal Asig(Guid sesionId, SemanaAcademica semana, Guid bloqueId) =>
            new(Guid.NewGuid(), sesionId, semana, bloqueId, null, Modalidad.Virtual);

        private static AsignarDocenteSesionService CrearServicio(
            FakeSesionRepo sesiones,
            FakeAsignacionRepo asignaciones,
            FakeBloqueRepo bloques,
            FakeDocenteRepo docentes) =>
            new(sesiones, asignaciones, bloques, docentes);

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Asignar_SinSolapeNiAdvertencias_RetornaOk()
        {
            var bloque  = Bloque(DiaDeSemana.Lunes, 7);
            var sesion  = CrearSesion(bloque.Id, 2m);
            var docente = CrearDocente();
            var asig    = Asig(sesion.Id, SemanaAcademica.A, bloque.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesion),
                new FakeAsignacionRepo(asig),
                new FakeBloqueRepo(bloque),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesion.Id, DocenteId = docente.Id };
            var res = await svc.EjecutarAsync(req);

            Assert.Equal(sesion.Id.ToString(), res.SesionId);
            Assert.Equal(docente.Id.ToString(), res.DocenteId);
            Assert.Empty(res.Advertencias);
            // La sesión queda con el docente asignado.
            Assert.Equal(docente.Id, sesion.DocenteId);
        }

        [Fact]
        public async Task Asignar_Desasignar_LimpiaSesion()
        {
            var bloque  = Bloque(DiaDeSemana.Lunes, 7);
            var docente = CrearDocente();
            var sesion  = CrearSesion(bloque.Id, 2m, docenteId: docente.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesion),
                new FakeAsignacionRepo(),
                new FakeBloqueRepo(bloque),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesion.Id, DocenteId = null };
            var res = await svc.EjecutarAsync(req);

            Assert.Null(res.DocenteId);
            Assert.Empty(res.Advertencias);
            Assert.Null(sesion.DocenteId);
        }

        [Fact]
        public async Task Asignar_SesionNoExiste_LanzaKeyNotFound()
        {
            var docente = CrearDocente();
            var svc = CrearServicio(
                new FakeSesionRepo(),
                new FakeAsignacionRepo(),
                new FakeBloqueRepo(),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = Guid.NewGuid(), DocenteId = docente.Id };

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.EjecutarAsync(req));
        }

        [Fact]
        public async Task Asignar_DocenteNoExiste_LanzaKeyNotFound()
        {
            var bloque  = Bloque(DiaDeSemana.Lunes, 7);
            var sesion  = CrearSesion(bloque.Id);
            var svc = CrearServicio(
                new FakeSesionRepo(sesion),
                new FakeAsignacionRepo(),
                new FakeBloqueRepo(bloque),
                new FakeDocenteRepo());

            var req = new AsignarDocenteRequest { SesionId = sesion.Id, DocenteId = Guid.NewGuid() };

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.EjecutarAsync(req));
        }

        // HC-I01 (edición): solape duro. Dos sesiones del mismo docente que se solapan en franja.
        // sesionTarget  [07:00-09:00] y sesionExistente [08:00-09:00] → se solapan → 409.
        [Fact]
        public async Task Asignar_SolapeEnFranja_LanzaInvalidOperation()
        {
            var bloqueA = Bloque(DiaDeSemana.Lunes, 7); // 07:00-08:00
            var bloqueB = Bloque(DiaDeSemana.Lunes, 8); // 08:00-09:00
            var docente = CrearDocente();

            var sesionTarget    = CrearSesion(bloqueA.Id, 2m);       // span 07:00-09:00
            var sesionExistente = CrearSesion(bloqueB.Id, 1m, docente.Id); // span 08:00-09:00

            var asigTarget    = Asig(sesionTarget.Id,    SemanaAcademica.A, bloqueA.Id);
            var asigExistente = Asig(sesionExistente.Id, SemanaAcademica.A, bloqueB.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesionTarget, sesionExistente),
                new FakeAsignacionRepo(asigTarget, asigExistente),
                new FakeBloqueRepo(bloqueA, bloqueB),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesionTarget.Id, DocenteId = docente.Id };

            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EjecutarAsync(req));
        }

        // HC-I01 negativo: sesiones adyacentes (sin solapamiento) → factible.
        [Fact]
        public async Task Asignar_SesionesAdyacentes_NoLanza()
        {
            var bloqueA = Bloque(DiaDeSemana.Lunes, 7);  // 07:00-08:00
            var bloqueB = Bloque(DiaDeSemana.Lunes, 9);  // 09:00-10:00
            var docente = CrearDocente();

            var sesionTarget    = CrearSesion(bloqueA.Id, 2m);       // span 07:00-09:00
            var sesionExistente = CrearSesion(bloqueB.Id, 1m, docente.Id); // span 09:00-10:00

            var asigTarget    = Asig(sesionTarget.Id,    SemanaAcademica.A, bloqueA.Id);
            var asigExistente = Asig(sesionExistente.Id, SemanaAcademica.A, bloqueB.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesionTarget, sesionExistente),
                new FakeAsignacionRepo(asigTarget, asigExistente),
                new FakeBloqueRepo(bloqueA, bloqueB),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesionTarget.Id, DocenteId = docente.Id };
            var res = await svc.EjecutarAsync(req);

            Assert.Equal(docente.Id.ToString(), res.DocenteId);
        }

        // HC-I02 soft: bloque no declarado en disponibilidad → advertencia (no rechazo).
        [Fact]
        public async Task Asignar_FueraDeDisponibilidad_PermiteConAdvertencia()
        {
            var bloqueAsignado  = Bloque(DiaDeSemana.Lunes, 7);  // no está en disponibles
            var bloqueDisponible = Bloque(DiaDeSemana.Lunes, 10); // sí está en disponibles
            var docente = CrearDocente(disponible: bloqueDisponible);
            var sesion  = CrearSesion(bloqueAsignado.Id, 1m);
            var asig    = Asig(sesion.Id, SemanaAcademica.A, bloqueAsignado.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesion),
                new FakeAsignacionRepo(asig),
                new FakeBloqueRepo(bloqueAsignado, bloqueDisponible),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesion.Id, DocenteId = docente.Id };
            var res = await svc.EjecutarAsync(req);

            Assert.Equal(docente.Id.ToString(), res.DocenteId);
            Assert.Single(res.Advertencias);
            Assert.Contains("disponibilidad", res.Advertencias[0], StringComparison.OrdinalIgnoreCase);
        }

        // HC-I03 soft: carga que supera el máximo → advertencia (no rechazo).
        [Fact]
        public async Task Asignar_CargaExcedida_PermiteConAdvertencia()
        {
            var bloqueTarget    = Bloque(DiaDeSemana.Lunes, 7);
            var bloqueExistente = Bloque(DiaDeSemana.Lunes, 10);
            // maxHoras = 2h, sesionExistente = 1h, sesionTarget = 2h → total 3h > 2h
            var docente = CrearDocente(maxHoras: 2m);

            var sesionTarget    = CrearSesion(bloqueTarget.Id, 2m);
            var sesionExistente = CrearSesion(bloqueExistente.Id, 1m, docente.Id);

            var asigTarget    = Asig(sesionTarget.Id,    SemanaAcademica.A, bloqueTarget.Id);
            var asigExistente = Asig(sesionExistente.Id, SemanaAcademica.A, bloqueExistente.Id);

            var svc = CrearServicio(
                new FakeSesionRepo(sesionTarget, sesionExistente),
                new FakeAsignacionRepo(asigTarget, asigExistente),
                new FakeBloqueRepo(bloqueTarget, bloqueExistente),
                new FakeDocenteRepo(docente));

            var req = new AsignarDocenteRequest { SesionId = sesionTarget.Id, DocenteId = docente.Id };
            var res = await svc.EjecutarAsync(req);

            Assert.Equal(docente.Id.ToString(), res.DocenteId);
            Assert.Single(res.Advertencias);
            Assert.Contains("máximo", res.Advertencias[0], StringComparison.OrdinalIgnoreCase);
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
            public Task AddRangeAsync(IEnumerable<Sesion> sesiones) { foreach (var s in sesiones) _store[s.Id] = s; return Task.CompletedTask; }
            public Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId) =>
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
    }
}
