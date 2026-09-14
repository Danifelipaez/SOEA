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
    /// G4 (bug reportado "error en el conteo de horas por docente"): la carga semanal (HC-I03
    /// blanda, también el solape duro HC-I01) se calculaba con TODAS las sesiones de la tabla,
    /// sin acotar por horario. Cada POST /horario/generar AGREGA un juego nuevo de sesiones y
    /// nunca borra el anterior (<c>GenerarHorarioService.AddRangeAsync</c>, sin delete previo),
    /// así que la carga de un docente crecía sin límite con cada regeneración. La corrección
    /// excluye las sesiones que pertenecen a un <see cref="Horario"/> distinto del de la sesión
    /// que se está editando — pero conserva las que no pertenecen a ninguno (p. ej. una sesión
    /// creada a mano, <c>CrearSesionManualService</c>, que nunca tiene Horario).
    /// </summary>
    public class AsignarDocenteSesionServiceHorarioScopeTests
    {
        private static BloqueTiempo Bloque(DiaDeSemana dia, int hora) =>
            new(Guid.NewGuid(), dia, new TimeOnly(hora, 0), new TimeOnly(hora + 1, 0));

        private static Sesion CrearSesion(Guid bloqueId, decimal duracion, Guid? docenteId) =>
            new(Guid.NewGuid(), Guid.NewGuid(), docenteId, bloqueId, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, duracion, false, false);

        private static Docente CrearDocente(decimal maxHoras)
        {
            var id = Guid.NewGuid();
            return new Docente(id, "Doc", "", $"doc-{id}@soea.edu", maxHoras,
                new List<FranjaHoraria> { FranjaHoraria.Matutino });
        }

        private static AsignacionSemanal Asig(Guid sesionId, Guid bloqueId) =>
            new(Guid.NewGuid(), sesionId, SemanaAcademica.A, bloqueId, null, Modalidad.Virtual);

        [Fact]
        public async Task SesionDeUnHorarioSuperado_NoCuentaParaLaCargaDelDocente()
        {
            var docente = CrearDocente(maxHoras: 3m);
            var bloqueViejo = Bloque(DiaDeSemana.Lunes, 7);
            var bloqueNuevo = Bloque(DiaDeSemana.Martes, 7);

            // Sesión de una generación anterior (superada): mismo docente, 2h.
            var sesionVieja = CrearSesion(bloqueViejo.Id, 2m, docente.Id);
            var horarioViejo = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { sesionVieja.Id });

            // Sesión de la generación actual, sin docente todavía — es la que se va a asignar.
            var sesionActual = CrearSesion(bloqueNuevo.Id, 2m, docenteId: null);
            var horarioActual = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { sesionActual.Id });
            var asigActual = Asig(sesionActual.Id, bloqueNuevo.Id);

            var svc = new AsignarDocenteSesionService(
                new FakeSesionRepo(sesionVieja, sesionActual),
                new FakeAsignacionRepo(asigActual),
                new FakeBloqueRepo(bloqueViejo, bloqueNuevo),
                new FakeDocenteRepo(docente),
                new FakeHorarioRepo(horarioViejo, horarioActual));

            var res = await svc.EjecutarAsync(new AsignarDocenteRequest { SesionId = sesionActual.Id, DocenteId = docente.Id });

            // Sin el fix: 2h (vieja) + 2h (nueva) = 4h > 3h máximo → advertencia falsa que crece
            // con cada regeneración. Con el fix: la sesión vieja es de otro horario → no cuenta.
            Assert.DoesNotContain(res.Advertencias, a => a.Contains("máximo de horas"));
        }

        [Fact]
        public async Task SesionSinHorario_SigueContandoParaLaCarga()
        {
            var docente = CrearDocente(maxHoras: 3m);
            var bloqueManual = Bloque(DiaDeSemana.Lunes, 7);
            var bloqueNuevo = Bloque(DiaDeSemana.Martes, 7);

            // Sesión manual (CrearSesionManualService no crea Horario): no pertenece a ninguno,
            // pero sigue siendo carga real del docente — no debe excluirse.
            var sesionManual = CrearSesion(bloqueManual.Id, 2m, docente.Id);

            var sesionActual = CrearSesion(bloqueNuevo.Id, 2m, docenteId: null);
            var horarioActual = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { sesionActual.Id });
            var asigActual = Asig(sesionActual.Id, bloqueNuevo.Id);

            var svc = new AsignarDocenteSesionService(
                new FakeSesionRepo(sesionManual, sesionActual),
                new FakeAsignacionRepo(asigActual),
                new FakeBloqueRepo(bloqueManual, bloqueNuevo),
                new FakeDocenteRepo(docente),
                new FakeHorarioRepo(horarioActual));

            var res = await svc.EjecutarAsync(new AsignarDocenteRequest { SesionId = sesionActual.Id, DocenteId = docente.Id });

            Assert.Contains(res.Advertencias, a => a.Contains("máximo de horas"));
        }

        /// <summary>
        /// Regresión (auditoría de limpieza, hallazgo 1.4): `IdsDeOtrosHorariosAsync` calculaba
        /// `propio` (el Horario al que pertenece la sesión editada) y excluía todo lo que NO
        /// fuera `propio`. Cuando la sesión editada no pertenece a ningún horario (una sesión
        /// manual, como aquí), `propio` es null — y `h.Id != propio?.Id` es cierto para
        /// CUALQUIER Guid real (no hay Guid que sea igual a null), así que la comparación
        /// excluía TODOS los horarios existentes del chequeo, no ninguno. El síntoma: asignar
        /// docente a una sesión manual nunca detectaba solape ni carga contra sesiones generadas
        /// por el pipeline. Habría fallado antes del fix (0 advertencias en vez de 1).
        /// </summary>
        [Fact]
        public async Task SesionEditadaSinHorario_NoExcluyeSesionesDeHorariosExistentes()
        {
            var docente = CrearDocente(maxHoras: 3m);
            var bloqueGenerado = Bloque(DiaDeSemana.Lunes, 7);
            var bloqueManual = Bloque(DiaDeSemana.Martes, 7);

            // Sesión generada por el pipeline, con Horario propio, mismo docente, 2h.
            var sesionGenerada = CrearSesion(bloqueGenerado.Id, 2m, docente.Id);
            var horarioGenerado = new SOEA.Domain.Entities.Horario(
                Guid.NewGuid(), "2026-1", new List<Guid> { sesionGenerada.Id });

            // La sesión que se está editando NO pertenece a ningún Horario (creada a mano).
            var sesionManual = CrearSesion(bloqueManual.Id, 2m, docenteId: null);
            var asigManual = Asig(sesionManual.Id, bloqueManual.Id);

            var svc = new AsignarDocenteSesionService(
                new FakeSesionRepo(sesionGenerada, sesionManual),
                new FakeAsignacionRepo(asigManual),
                new FakeBloqueRepo(bloqueGenerado, bloqueManual),
                new FakeDocenteRepo(docente),
                new FakeHorarioRepo(horarioGenerado));

            var res = await svc.EjecutarAsync(new AsignarDocenteRequest { SesionId = sesionManual.Id, DocenteId = docente.Id });

            // 2h (generada) + 2h (manual) = 4h > 3h máximo del docente → debe advertir.
            Assert.Contains(res.Advertencias, a => a.Contains("máximo de horas"));
        }

        [Fact]
        public async Task SinRepositorioDeHorarios_CaeAlComportamientoAnterior_SinAcotar()
        {
            // Compatibilidad: si el parámetro opcional no se provee (como en los tests
            // existentes de AsignarDocenteSesionServiceTests), el servicio no debe reventar.
            var docente = CrearDocente(maxHoras: 100m);
            var bloque = Bloque(DiaDeSemana.Lunes, 7);
            var sesion = CrearSesion(bloque.Id, 2m, docenteId: null);
            var asig = Asig(sesion.Id, bloque.Id);

            var svc = new AsignarDocenteSesionService(
                new FakeSesionRepo(sesion),
                new FakeAsignacionRepo(asig),
                new FakeBloqueRepo(bloque),
                new FakeDocenteRepo(docente));
            // horarios: se omite (default null)

            var res = await svc.EjecutarAsync(new AsignarDocenteRequest { SesionId = sesion.Id, DocenteId = docente.Id });

            Assert.Equal(docente.Id.ToString(), res.DocenteId);
        }

        // ── Fakes locales (mismo shape que AsignarDocenteSesionServiceTests.cs) ────────────

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

        private sealed class FakeHorarioRepo : IHorarioRepositorio
        {
            private readonly List<SOEA.Domain.Entities.Horario> _store = new();
            public FakeHorarioRepo(params SOEA.Domain.Entities.Horario[] horarios) => _store.AddRange(horarios);
            public Task<SOEA.Domain.Entities.Horario?> GetByIdAsync(Guid id) => Task.FromResult(_store.FirstOrDefault(h => h.Id == id));
            public Task<SOEA.Domain.Entities.Horario?> GetBySemestreAsync(string semestre) => Task.FromResult(_store.FirstOrDefault(h => h.Semestre == semestre));
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllAsync() => Task.FromResult(_store.ToList());
            public Task<List<SOEA.Domain.Entities.Horario>> GetAllBySemestreAsync(string semestre) => Task.FromResult(_store.Where(h => h.Semestre == semestre).ToList());
            public Task AddAsync(SOEA.Domain.Entities.Horario horario) { _store.Add(horario); return Task.CompletedTask; }
            public Task UpdateAsync(SOEA.Domain.Entities.Horario horario) => Task.CompletedTask;
        }
    }
}
