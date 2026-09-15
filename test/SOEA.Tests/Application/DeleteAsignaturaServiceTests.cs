using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Asignaturas;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Sesiones;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// AsignaturaService.DeleteAsync no revisaba si existían Grupos referenciando la asignatura
    /// antes de borrarla — sin FK a nivel de BD (Grupos.asignatura_id no tiene ninguna restricción),
    /// el borrado siempre tenía éxito y dejaba esos Grupos huérfanos (AsignaturaId apuntando a un
    /// registro que ya no existe). El síntoma: GenerarHorarioService los excluía en silencio de la
    /// generación de horario, identificados solo por Nombre (no único). Ahora el borrado elimina
    /// primero los Grupos asociados y luego la Asignatura, en vez de dejarlos huérfanos o bloquear.
    ///
    /// Tampoco purgaba las Sesiones generadas que referencian la asignatura o sus grupos —
    /// Sesion.AsignaturaId/GrupoId son FK Restrict (M14 auditoría), así que borrar una asignatura
    /// con sesiones generadas fallaba con el 409 genérico de EF en vez de eliminarlas: son datos
    /// regenerables de una corrida, no catálogo.
    /// </summary>
    public class DeleteAsignaturaServiceTests
    {
        private static Asignatura Existente(Guid id) =>
            new(id, "Bioquímica", "BIO201", 2, 1, 8, Guid.NewGuid());

        private static SesionCascadeService Cascade(FakeSesionRepo sesiones, FakeAsignacionRepo asignaciones) =>
            new(sesiones, asignaciones);

        private static Sesion SesionDe(Guid asignaturaId, Guid? grupoId) =>
            new(Guid.NewGuid(), asignaturaId, null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false);

        [Fact]
        public async Task Elimina_SiNoTieneGruposAsociados()
        {
            var asig = Existente(Guid.NewGuid());
            var asigRepo = new FakeAsignaturaRepo(asig);
            var grupoRepo = new FakeGrupoRepo();
            var service = new AsignaturaService(
                asigRepo, grupoRepo, Cascade(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await service.DeleteAsync(asig.Id);

            Assert.True(asigRepo.Eliminado);
        }

        [Fact]
        public async Task LanzaKeyNotFound_SiLaAsignaturaNoExiste()
        {
            var service = new AsignaturaService(
                new FakeAsignaturaRepo(), new FakeGrupoRepo(),
                Cascade(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.DeleteAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task EliminaGruposAsociados_YLuegoLaAsignatura_SiTieneGruposAsociados()
        {
            var asig = Existente(Guid.NewGuid());
            var asigRepo = new FakeAsignaturaRepo(asig);
            var grupo1 = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: asig.Id);
            var grupo2 = new Grupo(Guid.NewGuid(), "G2", Guid.Empty, 30, asignaturaId: asig.Id);
            var grupoOtraAsignatura = new Grupo(Guid.NewGuid(), "G3", Guid.Empty, 30, asignaturaId: Guid.NewGuid());
            var grupoRepo = new FakeGrupoRepo(grupo1, grupo2, grupoOtraAsignatura);
            var service = new AsignaturaService(
                asigRepo, grupoRepo, Cascade(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await service.DeleteAsync(asig.Id);

            Assert.True(asigRepo.Eliminado);
            Assert.Null(await grupoRepo.GetByIdAsync(grupo1.Id));
            Assert.Null(await grupoRepo.GetByIdAsync(grupo2.Id));
            // Los grupos de otras asignaturas no deben verse afectados.
            Assert.NotNull(await grupoRepo.GetByIdAsync(grupoOtraAsignatura.Id));
        }

        [Fact]
        public async Task EliminaSesionesYAsignacionesGeneradas_EnVezDeFallar_SiExisten()
        {
            var asig = Existente(Guid.NewGuid());
            var asigRepo = new FakeAsignaturaRepo(asig);
            var grupo = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: asig.Id);
            var grupoRepo = new FakeGrupoRepo(grupo);

            // Una sesión generada por el grupo y otra directa por la asignatura (sin grupo).
            var sesionPorGrupo = SesionDe(asig.Id, grupo.Id);
            var sesionDirecta = SesionDe(asig.Id, null);
            var sesionRepo = new FakeSesionRepo(sesionPorGrupo, sesionDirecta);
            var asignacionRepo = new FakeAsignacionRepo(
                new AsignacionSemanal(Guid.NewGuid(), sesionPorGrupo.Id, SemanaAcademica.A, Guid.NewGuid(), null, Modalidad.Presencial),
                new AsignacionSemanal(Guid.NewGuid(), sesionDirecta.Id, SemanaAcademica.A, Guid.NewGuid(), null, Modalidad.Presencial));

            var service = new AsignaturaService(asigRepo, grupoRepo, Cascade(sesionRepo, asignacionRepo), new FakeUnitOfWork());

            await service.DeleteAsync(asig.Id);

            Assert.True(asigRepo.Eliminado);
            Assert.Null(await grupoRepo.GetByIdAsync(grupo.Id));
            Assert.Empty(await sesionRepo.GetAllAsync());
            Assert.Empty(await asignacionRepo.GetAllAsync());
        }

        // ── Repos fake ───────────────────────────────────────────────────────────

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();
            public bool Eliminado { get; private set; }

            public FakeAsignaturaRepo(params Asignatura[] seed)
            {
                foreach (var a in seed) _store[a.Id] = a;
            }

            public Task<Asignatura?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<Asignatura?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo));
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo && a.ProgramaId == programaId));
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Nombre == nombre && a.ProgramaId == programaId));
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); Eliminado = true; return Task.CompletedTask; }
        }

        private sealed class FakeGrupoRepo : IGrupoRepositorio
        {
            private readonly List<Grupo> _grupos;
            public FakeGrupoRepo(params Grupo[] seed) => _grupos = seed.ToList();

            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult(_grupos.FirstOrDefault(g => g.Id == id));
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(_grupos.ToList());
            public Task AddAsync(Grupo entity) { _grupos.Add(entity); return Task.CompletedTask; }
            public Task UpdateAsync(Grupo entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) { _grupos.RemoveAll(g => g.Id == id); return Task.CompletedTask; }
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
                Task.FromResult(_grupos.FirstOrDefault(g => g.Nombre == nombre && g.ProgramaId == programaId));
            public Task<Grupo?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_grupos.FirstOrDefault(g => g.Codigo == codigo));
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) =>
                Task.FromResult(_grupos.Where(g => g.AsignaturaId == asignaturaId).AsEnumerable());
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) =>
                Task.FromResult(_grupos.Where(g => g.DocenteId == docenteId).AsEnumerable());
        }
    }
}
