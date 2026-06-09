using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.TiposAlternancia;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Catálogo de tipos de alternancia (Inc. C): mapeo a patrón base, protección de tipos de
    /// sistema y CRUD del servicio. Usa un repositorio fake en memoria.
    /// </summary>
    public class TipoAlternanciaConfigServiceTests
    {
        [Theory]
        [InlineData(PatronBaseAlternancia.PresencialEnSemanaA, TipoAlternancia.TipoA)]
        [InlineData(PatronBaseAlternancia.PresencialEnSemanaB, TipoAlternancia.TipoB)]
        [InlineData(PatronBaseAlternancia.SinAlternancia,      TipoAlternancia.SinAlternancia)]
        public void ResolverTipoAlternancia_MapeaPatronBase(PatronBaseAlternancia patron, TipoAlternancia esperado)
        {
            var tipo = new TipoAlternanciaConfig(Guid.NewGuid(), "X", patron);
            Assert.Equal(esperado, tipo.ResolverTipoAlternancia());
        }

        [Fact]
        public void TipoDeSistema_NoCambiaPatronBaseAlActualizar()
        {
            var tipo = new TipoAlternanciaConfig(
                TipoAlternanciaConfig.IdTipoA, "Tipo A", PatronBaseAlternancia.PresencialEnSemanaA, 8, "#1565c0", esSistema: true);

            tipo.ActualizarDatos("Tipo A renombrado", PatronBaseAlternancia.PresencialEnSemanaB, 11, "#000000", true);

            Assert.Equal("Tipo A renombrado", tipo.Nombre);
            Assert.Equal(11, tipo.SemanasPresenciales);
            Assert.Equal(PatronBaseAlternancia.PresencialEnSemanaA, tipo.PatronBase); // patrón base intacto
        }

        [Fact]
        public async Task Create_PersisteYMapea()
        {
            var repo = new FakeRepo();
            var svc = new TipoAlternanciaConfigService(repo);

            var creado = await svc.CreateAsync(new TipoAlternanciaConfigDto
            {
                Nombre = "Química Orgánica", PatronBase = "PresencialEnSemanaA", SemanasPresenciales = 8, Color = "#abcdef"
            });

            Assert.False(creado.EsSistema);
            Assert.Equal("PresencialEnSemanaA", creado.PatronBase);
            Assert.NotNull(await repo.GetByIdAsync(creado.Id));
        }

        [Fact]
        public async Task Delete_TipoSistema_Lanza()
        {
            var repo = new FakeRepo();
            await repo.AddAsync(new TipoAlternanciaConfig(
                TipoAlternanciaConfig.IdTipoA, "Tipo A", PatronBaseAlternancia.PresencialEnSemanaA, 8, "#1565c0", esSistema: true));
            var svc = new TipoAlternanciaConfigService(repo);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.DeleteAsync(TipoAlternanciaConfig.IdTipoA));
        }

        [Fact]
        public async Task Delete_TipoCustom_Elimina()
        {
            var repo = new FakeRepo();
            var svc = new TipoAlternanciaConfigService(repo);
            var creado = await svc.CreateAsync(new TipoAlternanciaConfigDto { Nombre = "Custom", PatronBase = "SinAlternancia" });

            var ok = await svc.DeleteAsync(creado.Id);

            Assert.True(ok);
            Assert.Null(await repo.GetByIdAsync(creado.Id));
        }

        private sealed class FakeRepo : ITipoAlternanciaConfigRepositorio
        {
            private readonly Dictionary<Guid, TipoAlternanciaConfig> _store = new();
            public Task AddAsync(TipoAlternanciaConfig e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task<TipoAlternanciaConfig?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<TipoAlternanciaConfig>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task UpdateAsync(TipoAlternanciaConfig e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
        }
    }
}
