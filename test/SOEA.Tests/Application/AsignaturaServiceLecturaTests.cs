using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// GetByIdAsync/GetAllAsync/UpdateElegibilidadAlternanciaAsync no tenían test dedicado (solo
    /// CreateAsync/UpdateAsync vía UpdateAsignaturaServiceTests, y DeleteAsync vía
    /// DeleteAsignaturaServiceTests). ERR1/ERR2 auditoría: las cuatro rutas de "no encontrado"
    /// del servicio lanzan KeyNotFoundException (antes GetByIdAsync/UpdateElegibilidadAlternanciaAsync
    /// usaban InvalidOperationException, una excepción distinta para el mismo caso que DeleteAsync).
    /// </summary>
    public class AsignaturaServiceLecturaTests
    {
        private static Asignatura Existente(Guid id, string nombre = "Bioquímica") =>
            new(id, nombre, "BIO201", 2, 1, 8, Guid.NewGuid());

        [Fact]
        public async Task GetByIdAsync_AsignaturaExistente_DevuelveResponse()
        {
            var asig = Existente(Guid.NewGuid());
            var service = new AsignaturaService(new FakeAsignaturaRepo(asig), new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            var response = await service.GetByIdAsync(asig.Id);

            Assert.Equal(asig.Id, response.Id);
            Assert.Equal("Bioquímica", response.Nombre);
        }

        [Fact]
        public async Task GetByIdAsync_NoExiste_LanzaKeyNotFound()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.GetByIdAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetAllAsync_ListaVacia_DevuelveListaVaciaNoNull()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            var response = await service.GetAllAsync();

            Assert.NotNull(response);
            Assert.Empty(response);
        }

        [Fact]
        public async Task GetAllAsync_ConVariasAsignaturas_DevuelveTodas()
        {
            var a1 = Existente(Guid.NewGuid(), "Bioquímica");
            var a2 = Existente(Guid.NewGuid(), "Cálculo I");
            var service = new AsignaturaService(new FakeAsignaturaRepo(a1, a2), new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            var response = await service.GetAllAsync();

            Assert.Equal(2, response.Count);
            Assert.Contains(response, r => r.Nombre == "Bioquímica");
            Assert.Contains(response, r => r.Nombre == "Cálculo I");
        }

        [Fact]
        public async Task UpdateElegibilidadAlternanciaAsync_CambiaSoloEseCampo()
        {
            var asig = Existente(Guid.NewGuid());
            asig.EstablecerAlternancia(TipoAlternancia.TipoA);
            var repo = new FakeAsignaturaRepo(asig);
            var service = new AsignaturaService(repo, new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await service.UpdateElegibilidadAlternanciaAsync(asig.Id, true);

            var actualizada = await repo.GetByIdAsync(asig.Id);
            Assert.True(actualizada!.EsCandidataAlternancia);
            // No debe tocar otros campos independientes de la elegibilidad.
            Assert.Equal(TipoAlternancia.TipoA, actualizada.Alternancia);
            Assert.Equal("Bioquímica", actualizada.Nombre);
        }

        [Fact]
        public async Task UpdateElegibilidadAlternanciaAsync_NoExiste_LanzaKeyNotFound()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.UpdateElegibilidadAlternanciaAsync(Guid.NewGuid(), true));
        }

        // ── Repos fake (mismo patrón que DeleteAsignaturaServiceTests) ──────────────

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();

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
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
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
