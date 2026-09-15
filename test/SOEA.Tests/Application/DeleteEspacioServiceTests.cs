using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Espacios;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;
using SOEA.Domain.ValueObjects;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// EspacioService.DeleteAsync no existía como tal (EspaciosController borraba directo por
    /// repositorio) y no revisaba si algún Grupo.RequisitosEspacio exigía este espacio antes de
    /// borrarlo — esa columna es JSON, sin FK en BD, así que el borrado siempre tenía éxito y
    /// dejaba esos grupos exigiendo un espacio inexistente. Mismo bug que
    /// DeleteAsignaturaServiceTests documenta para Asignatura→Grupo; este guard lo corrige para
    /// Espacio→Grupo.RequisitosEspacio.
    ///
    /// Sesion.EspacioId sí es FK Restrict (M14 auditoría): el comentario original decía "no revisa
    /// Sesiones a propósito" asumiendo que no bloqueaban el borrado, pero con esa FK real un espacio
    /// con sesiones generadas fallaba igual con el 409 genérico de EF. Ahora se purgan en cascada:
    /// son datos regenerables de una corrida, no catálogo.
    /// </summary>
    public class DeleteEspacioServiceTests
    {
        private static Espacio Existente(Guid id) => new(id, "Lab 1", TipoEspacio.Laboratorio, 30);

        private static Grupo GrupoConRequisito(Guid? espacioId)
        {
            var g = new Grupo(Guid.NewGuid(), "Grupo " + Guid.NewGuid().ToString("N")[..4], Guid.NewGuid(), 30);
            g.ActualizarRequisitosEspacio(new List<RequisitoEspacio>
            {
                new(TipoSesion.Laboratorio, espacioId, null, 1)
            });
            return g;
        }

        private static Sesion SesionDelEspacio(Guid espacioId) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), espacioId, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false);

        private static EspacioService Servicio(FakeEspacioRepo espacioRepo, FakeGrupoRepo grupoRepo, SesionCascadeService? cascade = null) =>
            new(espacioRepo, grupoRepo, cascade ?? new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

        [Fact]
        public async Task Elimina_SiNingunGrupoLoExigeComoRequisito()
        {
            var espacio = Existente(Guid.NewGuid());
            var espacioRepo = new FakeEspacioRepo(espacio);
            var grupoRepo = new FakeGrupoRepo();
            var service = Servicio(espacioRepo, grupoRepo);

            await service.DeleteAsync(espacio.Id);

            Assert.Null(await espacioRepo.GetByIdAsync(espacio.Id));
        }

        [Fact]
        public async Task LanzaKeyNotFound_SiElEspacioNoExiste()
        {
            var service = Servicio(new FakeEspacioRepo(), new FakeGrupoRepo());

            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => service.DeleteAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task LanzaBusinessRuleViolation_ConConteo_SiAlgunGrupoLoExigeComoRequisito()
        {
            var espacio = Existente(Guid.NewGuid());
            var espacioRepo = new FakeEspacioRepo(espacio);
            var grupoRepo = new FakeGrupoRepo(
                GrupoConRequisito(espacio.Id),
                GrupoConRequisito(espacio.Id),
                GrupoConRequisito(Guid.NewGuid())); // otro espacio, no cuenta
            var service = Servicio(espacioRepo, grupoRepo);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => service.DeleteAsync(espacio.Id));

            Assert.Contains("2", ex.Message);
            Assert.NotNull(await espacioRepo.GetByIdAsync(espacio.Id)); // el guard no debe tener fugas
        }

        [Fact]
        public async Task EliminaSesionesYAsignacionesGeneradas_EnVezDeFallar_SiExisten()
        {
            var espacio = Existente(Guid.NewGuid());
            var espacioRepo = new FakeEspacioRepo(espacio);
            var sesion = SesionDelEspacio(espacio.Id);
            var sesionRepo = new FakeSesionRepo(sesion);
            var asignacionRepo = new FakeAsignacionRepo(
                new AsignacionSemanal(Guid.NewGuid(), sesion.Id, SemanaAcademica.A, Guid.NewGuid(), null, Modalidad.Presencial));
            var service = Servicio(espacioRepo, new FakeGrupoRepo(), new SesionCascadeService(sesionRepo, asignacionRepo));

            await service.DeleteAsync(espacio.Id);

            Assert.Null(await espacioRepo.GetByIdAsync(espacio.Id));
            Assert.Empty(await sesionRepo.GetAllAsync());
            Assert.Empty(await asignacionRepo.GetAllAsync());
        }

        // ── Repos fake ───────────────────────────────────────────────────────────

        private sealed class FakeEspacioRepo : IEspacioRepositorio
        {
            private readonly Dictionary<Guid, Espacio> _store = new();
            public FakeEspacioRepo(params Espacio[] seed) { foreach (var e in seed) _store[e.Id] = e; }
            public Task<Espacio?> GetByIdAsync(Guid id) => Task.FromResult(_store.GetValueOrDefault(id));
            public Task<List<Espacio>> GetAllAsync() => Task.FromResult(_store.Values.ToList());
            public Task AddAsync(Espacio e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task UpdateAsync(Espacio e) { _store[e.Id] = e; return Task.CompletedTask; }
            public Task DeleteAsync(Guid id) { _store.Remove(id); return Task.CompletedTask; }
            public Task<Espacio?> GetByNombreAsync(string nombre) =>
                Task.FromResult(_store.Values.FirstOrDefault(e => e.Nombre == nombre));
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
