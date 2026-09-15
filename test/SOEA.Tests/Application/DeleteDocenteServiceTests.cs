using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Docentes;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// DocenteService.DeleteAsync no revisaba si existían Grupos con este docente asignado antes
    /// de borrarlo — Grupo.docente_id no tiene FK en BD, así que el borrado siempre tenía éxito y
    /// dejaba esos Grupos apuntando a un docente inexistente. Mismo bug que
    /// DeleteAsignaturaServiceTests documenta para Asignatura→Grupo; este guard lo corrige para
    /// Docente→Grupo. No revisa Sesiones a propósito: son datos generados y transitorios
    /// (ver comentario en FusionDocentesService), no catálogo.
    /// </summary>
    public class DeleteDocenteServiceTests
    {
        private static Docente Existente(Guid id) =>
            new(id, "Ana Torres", "", $"{id}@x.com", 40m, new List<FranjaHoraria> { FranjaHoraria.Matutino });

        [Fact]
        public async Task Elimina_SiNingunGrupoLoTieneAsignado()
        {
            var docente = Existente(Guid.NewGuid());
            var docenteRepo = new FakeDocenteRepo(docente);
            var grupoRepo = new FakeGrupoRepo();
            var service = new DocenteService(docenteRepo, grupoRepo);

            var eliminado = await service.DeleteAsync(docente.Id);

            Assert.True(eliminado);
            Assert.Null(await docenteRepo.GetByIdAsync(docente.Id));
        }

        [Fact]
        public async Task DevuelveFalse_SiElDocenteNoExiste()
        {
            var service = new DocenteService(new FakeDocenteRepo(), new FakeGrupoRepo());

            var eliminado = await service.DeleteAsync(Guid.NewGuid());

            Assert.False(eliminado);
        }

        [Fact]
        public async Task LanzaBusinessRuleViolation_ConConteo_SiTieneGruposAsignados()
        {
            var docente = Existente(Guid.NewGuid());
            var docenteRepo = new FakeDocenteRepo(docente);
            var grupoRepo = new FakeGrupoRepo(
                new Grupo(Guid.NewGuid(), "G1", Guid.NewGuid(), 30, docenteId: docente.Id),
                new Grupo(Guid.NewGuid(), "G2", Guid.NewGuid(), 30, docenteId: docente.Id));
            var service = new DocenteService(docenteRepo, grupoRepo);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => service.DeleteAsync(docente.Id));

            Assert.Contains("2", ex.Message);
            Assert.NotNull(await docenteRepo.GetByIdAsync(docente.Id)); // el guard no debe tener fugas
        }

        // ── Repos fake ───────────────────────────────────────────────────────────

        private sealed class FakeDocenteRepo : IDocenteRepositorio
        {
            private readonly Dictionary<Guid, Docente> _store = new();
            public FakeDocenteRepo(params Docente[] seed) { foreach (var d in seed) _store[d.Id] = d; }
            public Task<Docente?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Docente>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Docente e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Docente?> GetByCedulaAsync(string cedula) =>
                Task.FromResult(_store.Values.FirstOrDefault(d => d.CedulaIdentidad == cedula));
            public Task<Docente?> GetByNombreAsync(string nombre) =>
                Task.FromResult(_store.Values.FirstOrDefault(d => d.Nombre == nombre));
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
