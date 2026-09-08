using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Verifica que el PUT de asignaturas actualice los campos editables (Bug 2 de Ingesta:
    /// las ediciones de duración se descartaban porque no existía vía de actualización).
    /// </summary>
    public class AsignaturaServiceTests
    {
        // Shape legado: 2h, 1 sesión/semana → mapea a teoría presencial.
        private static Asignatura Existente(Guid id, Guid progId) =>
            new(id, "Bioquímica", "BIO201", 2, 1, 8, progId);

        private static UpdateAsignaturaRequest Request(Guid progId) => new()
        {
            Nombre = "Bioquímica Avanzada",
            Codigo = "BIO201",
            SesionesTeoriaPresencialSemana = 2,
            HorasTeoriaPresencial = 3,
            SesionesLaboratorioSemestre = 11,
            ProgramaId = progId
        };

        [Fact]
        public async Task ActualizaCamposEditablesYPersiste()
        {
            var progId = Guid.NewGuid();
            var asig   = Existente(Guid.NewGuid(), progId);
            var repo   = new FakeAsignaturaRepo(asig);
            var service = new AsignaturaService(repo, new FakeGrupoRepoVacio());

            var response = await service.UpdateAsync(asig.Id, Request(progId));

            Assert.Equal(1, repo.Actualizaciones);
            Assert.Equal("Bioquímica Avanzada", response.Nombre);
            Assert.Equal(3, response.HorasTeoriaPresencial);
            Assert.Equal(2, response.SesionesTeoriaPresencialSemana);
            Assert.Equal(11, response.SesionesLaboratorioSemestre);
            // Sin alternancia explícita se infiere por umbral: 11 > 8 ⇒ TipoB.
            Assert.Equal(TipoAlternancia.TipoB, response.Alternancia);
        }

        [Fact]
        public async Task AlternanciaExplicita_SeRespetaSobreLaInferida()
        {
            var progId = Guid.NewGuid();
            var asig   = Existente(Guid.NewGuid(), progId);
            var service = new AsignaturaService(new FakeAsignaturaRepo(asig), new FakeGrupoRepoVacio());

            var request = Request(progId);
            request.Alternancia = TipoAlternancia.TipoA; // override manual (11 lab inferiría TipoB)

            var response = await service.UpdateAsync(asig.Id, request);

            Assert.Equal(TipoAlternancia.TipoA, response.Alternancia);
        }

        // ── P3.1: POST /api/asignaturas — id de cliente + Categoria/Alternancia explícitas ──

        private static CreateAsignaturaRequest CreateRequest(Guid progId) => new()
        {
            Nombre = "Física I",
            Codigo = "FIS101",
            SesionesTeoriaPresencialSemana = 2,
            HorasTeoriaPresencial = 2,
            ProgramaId = progId
        };

        [Fact]
        public async Task CreateAsync_ConIdDeCliente_LaRespeta()
        {
            var repo = new FakeAsignaturaRepo();
            var service = new AsignaturaService(repo, new FakeGrupoRepoVacio());
            var idCliente = Guid.NewGuid();

            var request = CreateRequest(Guid.NewGuid());
            request.Id = idCliente;

            var response = await service.CreateAsync(request);

            Assert.Equal(idCliente, response.Id);
        }

        [Fact]
        public async Task CreateAsync_SinIdDeCliente_GeneraUno()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepoVacio());

            var response = await service.CreateAsync(CreateRequest(Guid.NewGuid()));

            Assert.NotEqual(Guid.Empty, response.Id);
        }

        [Fact]
        public async Task CreateAsync_AplicaCategoriaYAlternanciaExplicitas()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepoVacio());
            var request = CreateRequest(Guid.NewGuid());
            request.Categoria = CategoriaAsignatura.Electiva;
            request.Alternancia = TipoAlternancia.TipoA;

            var response = await service.CreateAsync(request);

            Assert.Equal(CategoriaAsignatura.Electiva, response.Categoria);
            Assert.Equal(TipoAlternancia.TipoA, response.Alternancia);
        }

        [Fact]
        public async Task LanzaInvalidOperation_SiNoExiste()
        {
            var service = new AsignaturaService(new FakeAsignaturaRepo(), new FakeGrupoRepoVacio());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.UpdateAsync(Guid.NewGuid(), Request(Guid.NewGuid())));
        }

        [Fact]
        public async Task LanzaArgument_SiDatosInvalidos()
        {
            var progId  = Guid.NewGuid();
            var asig    = Existente(Guid.NewGuid(), progId);
            var service = new AsignaturaService(new FakeAsignaturaRepo(asig), new FakeGrupoRepoVacio());

            var request = Request(progId);
            request.HorasTeoriaPresencial = 0; // conteo > 0 con horas = 0 → el dominio exige horas > 0

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.UpdateAsync(asig.Id, request));
        }

        // ── Repo fake ────────────────────────────────────────────────────────────

        private sealed class FakeAsignaturaRepo : IAsignaturaRepositorio
        {
            private readonly Dictionary<Guid, Asignatura> _store = new();
            public int Actualizaciones { get; private set; }

            public FakeAsignaturaRepo(params Asignatura[] seed)
            {
                foreach (var a in seed) _store[a.Id] = a;
            }

            public Task<Asignatura?> GetByIdAsync(Guid id) =>
                Task.FromResult(_store.GetValueOrDefault(id));
            public Task<Asignatura?> GetByCodigoAsync(string codigo) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo));
            public Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a => a.Codigo == codigo && a.ProgramaId == programaId));
            public Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId) =>
                Task.FromResult(_store.Values.FirstOrDefault(a =>
                    a.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && a.ProgramaId == programaId));
            public Task<List<Asignatura>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(Asignatura e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Asignatura e) { _store[e.Id] = e; Actualizaciones++; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }

        /// <summary>Siempre vacío — estos tests no ejercitan el guard de Grupos asociados de
        /// AsignaturaService.DeleteAsync (ver DeleteAsignaturaServiceTests.cs).</summary>
        private sealed class FakeGrupoRepoVacio : IGrupoRepositorio
        {
            public Task<Grupo?> GetByIdAsync(Guid id) => Task.FromResult<Grupo?>(null);
            public Task<List<Grupo>> GetAllAsync() => Task.FromResult(new List<Grupo>());
            public Task AddAsync(Grupo entity) => Task.CompletedTask;
            public Task UpdateAsync(Grupo entity) => Task.CompletedTask;
            public Task DeleteAsync(Guid id) => Task.CompletedTask;
            public Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId) => Task.FromResult<Grupo?>(null);
            public Task<Grupo?> GetByCodigoAsync(string codigo) => Task.FromResult<Grupo?>(null);
            public Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId) => Task.FromResult(Enumerable.Empty<Grupo>());
            public Task<IEnumerable<Grupo>> GetByDocenteIdAsync(Guid docenteId) => Task.FromResult(Enumerable.Empty<Grupo>());
        }
    }
}
