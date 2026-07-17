using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Docentes;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Verifica la fusión manual de docentes duplicados: reasigna los GRUPOS de los duplicados
    /// al canónico y elimina los duplicados; y la sugerencia de grupos candidatos.
    /// (El docente vive en el Grupo, no en la Asignatura.)
    /// Usa repositorios fake en memoria (el proyecto de tests no referencia un mocking framework).
    /// </summary>
    public class FusionDocentesServiceTests
    {
        private static Docente Docente(Guid id, string nombre) =>
            new(id, nombre, "", $"{id}@x.com", 40m, new List<FranjaHoraria> { FranjaHoraria.Matutino });

        private static Grupo Grupo(Guid? docenteId)
        {
            var g = new Grupo(Guid.NewGuid(), "Grupo " + Guid.NewGuid().ToString("N")[..4], Guid.NewGuid(), 30);
            g.AsignarDocente(docenteId);
            return g;
        }

        private static FusionDocentesService Crear(
            FakeDocenteRepo docenteRepo, FakeGrupoRepo grupoRepo) =>
            new(docenteRepo, grupoRepo, new DocenteService(docenteRepo));

        [Fact]
        public async Task Fusionar_ReasignaGrupos_YEliminaDuplicados()
        {
            var canonico = Docente(Guid.NewGuid(), "Victor Macias Villamizar");
            var dup      = Docente(Guid.NewGuid(), "Victor Enrique Macias Vil");
            var docenteRepo = new FakeDocenteRepo(canonico, dup);

            var gDup  = Grupo(dup.Id);
            var gCan  = Grupo(canonico.Id);
            var gNull = Grupo(null);
            var grupoRepo = new FakeGrupoRepo(gDup, gCan, gNull);

            var svc = Crear(docenteRepo, grupoRepo);

            var r = await svc.FusionarAsync(canonico.Id, new[] { dup.Id });

            Assert.Equal(1, r.GruposReasignados);
            Assert.Equal(1, r.DocentesEliminados);
            Assert.Equal(canonico.Id, gDup.DocenteId);        // reasignado al canónico
            Assert.Equal(canonico.Id, gCan.DocenteId);        // intacto
            Assert.Null(gNull.DocenteId);                     // intacto
            Assert.Null(await docenteRepo.GetByIdAsync(dup.Id));      // duplicado eliminado
            Assert.NotNull(await docenteRepo.GetByIdAsync(canonico.Id)); // canónico conservado
        }

        [Fact]
        public async Task Fusionar_SinDuplicadosDistintos_Lanza()
        {
            var canonico = Docente(Guid.NewGuid(), "Ana Torres");
            var svc = Crear(new FakeDocenteRepo(canonico), new FakeGrupoRepo());

            await Assert.ThrowsAsync<ArgumentException>(
                () => svc.FusionarAsync(canonico.Id, new[] { canonico.Id }));
        }

        [Fact]
        public async Task Fusionar_CanonicoInexistente_Lanza()
        {
            var svc = Crear(new FakeDocenteRepo(), new FakeGrupoRepo());

            await Assert.ThrowsAsync<ArgumentException>(
                () => svc.FusionarAsync(Guid.NewGuid(), new[] { Guid.NewGuid() }));
        }

        [Fact]
        public async Task SugerirDuplicados_AgrupaVariantesDelMismoNombre()
        {
            var docenteRepo = new FakeDocenteRepo(
                Docente(Guid.NewGuid(), "Victor Enrique Macias Vil"),
                Docente(Guid.NewGuid(), "Victor Macias Villamizar"),
                Docente(Guid.NewGuid(), "Ana Torres"));

            var svc = Crear(docenteRepo, new FakeGrupoRepo());

            var grupos = await svc.SugerirDuplicadosAsync();

            Assert.Single(grupos);
            Assert.Equal(2, grupos[0].Count);
        }

        // ── Fakes en memoria ─────────────────────────────────────────────────────
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
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(g =>
                    g.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && g.ProgramaId == programaId));
            public Task<Grupo?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_store.Values.FirstOrDefault(g => g.Codigo == codigo));
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) =>
                Task.FromResult(_store.Values.Where(g => g.AsignaturaId == asignaturaId).AsEnumerable());
        }
    }
}
