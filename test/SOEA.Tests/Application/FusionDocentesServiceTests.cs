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
    /// Verifica la fusión manual de docentes duplicados: reasigna las asignaturas de los duplicados
    /// al canónico y elimina los duplicados; y la sugerencia de grupos candidatos.
    /// Usa repositorios fake en memoria (el proyecto de tests no referencia un mocking framework).
    /// </summary>
    public class FusionDocentesServiceTests
    {
        private static Docente Docente(Guid id, string nombre) =>
            new(id, nombre, "", $"{id}@x.com", 40m, new List<FranjaHoraria> { FranjaHoraria.Matutino });

        private static Asignatura Asignatura(Guid? docenteId)
        {
            var a = new Asignatura(Guid.NewGuid(), "Quimica", "QUI" + Guid.NewGuid().ToString("N")[..4], 2, 1, 8, Guid.NewGuid());
            a.AsignarDocente(docenteId);
            return a;
        }

        private static FusionDocentesService Crear(
            FakeDocenteRepo docenteRepo, FakeAsignaturaRepo asignaturaRepo) =>
            new(docenteRepo, asignaturaRepo, new DocenteService(docenteRepo));

        [Fact]
        public async Task Fusionar_ReasignaAsignaturas_YEliminaDuplicados()
        {
            var canonico = Docente(Guid.NewGuid(), "Victor Macias Villamizar");
            var dup      = Docente(Guid.NewGuid(), "Victor Enrique Macias Vil");
            var docenteRepo = new FakeDocenteRepo(canonico, dup);

            var aDup  = Asignatura(dup.Id);
            var aCan  = Asignatura(canonico.Id);
            var aNull = Asignatura(null);
            var asignaturaRepo = new FakeAsignaturaRepo(aDup, aCan, aNull);

            var svc = Crear(docenteRepo, asignaturaRepo);

            var r = await svc.FusionarAsync(canonico.Id, new[] { dup.Id });

            Assert.Equal(1, r.AsignaturasReasignadas);
            Assert.Equal(1, r.DocentesEliminados);
            Assert.Equal(canonico.Id, aDup.DocenteId);        // reasignada al canónico
            Assert.Equal(canonico.Id, aCan.DocenteId);        // intacta
            Assert.Null(aNull.DocenteId);                     // intacta
            Assert.Null(await docenteRepo.GetByIdAsync(dup.Id));      // duplicado eliminado
            Assert.NotNull(await docenteRepo.GetByIdAsync(canonico.Id)); // canónico conservado
        }

        [Fact]
        public async Task Fusionar_SinDuplicadosDistintos_Lanza()
        {
            var canonico = Docente(Guid.NewGuid(), "Ana Torres");
            var svc = Crear(new FakeDocenteRepo(canonico), new FakeAsignaturaRepo());

            await Assert.ThrowsAsync<ArgumentException>(
                () => svc.FusionarAsync(canonico.Id, new[] { canonico.Id }));
        }

        [Fact]
        public async Task Fusionar_CanonicoInexistente_Lanza()
        {
            var svc = Crear(new FakeDocenteRepo(), new FakeAsignaturaRepo());

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

            var svc = Crear(docenteRepo, new FakeAsignaturaRepo());

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

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();
            public FakeAsignaturaRepo(params Asignatura[] asignaturas) { foreach (var a in asignaturas) _store[a.Id] = a; }
            public Task AddAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<Asignatura?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Asignatura?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo));
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo && a.ProgramaId == programaId));
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a =>
                    a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && a.ProgramaId == programaId));
        }
    }
}
