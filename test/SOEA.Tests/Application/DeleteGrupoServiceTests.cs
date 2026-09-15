using System;
using System.Threading.Tasks;
using SOEA.Application.Features.Grupos;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// GruposController.Delete bloqueaba el borrado con 409 si el grupo tenía sesiones generadas
    /// ("Regenere el horario sin este grupo, o elimínelo primero de la corrida vigente" — workaround
    /// que no existe como endpoint). El catálogo no debe bloquearse por datos de una corrida: son
    /// regenerables, no catálogo (mismo principio que EspacioService/DocenteService.DeleteAsync).
    /// GrupoService.DeleteAsync purga esas sesiones (y sus AsignacionSemanal) en vez de bloquear.
    /// </summary>
    public class DeleteGrupoServiceTests
    {
        private static Grupo Existente(Guid id) => new(id, "G1", Guid.NewGuid(), 30, asignaturaId: Guid.NewGuid());

        private static Sesion SesionDelGrupo(Guid grupoId) =>
            new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false);

        [Fact]
        public async Task Elimina_SiNoTieneSesionesGeneradas()
        {
            var grupo = Existente(Guid.NewGuid());
            var grupoRepo = new FakeGrupoRepo(grupo);
            var service = new GrupoService(
                grupoRepo, new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await service.DeleteAsync(grupo.Id);

            Assert.Null(await grupoRepo.GetByIdAsync(grupo.Id));
        }

        [Fact]
        public async Task LanzaKeyNotFound_SiElGrupoNoExiste()
        {
            var service = new GrupoService(
                new FakeGrupoRepo(), new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());

            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task EliminaElGrupoYSusSesionesGeneradas_EnVezDeBloquear_SiTieneSesiones()
        {
            var grupo = Existente(Guid.NewGuid());
            var grupoRepo = new FakeGrupoRepo(grupo);
            var sesion = SesionDelGrupo(grupo.Id);
            var sesionRepo = new FakeSesionRepo(sesion);
            var asignacionRepo = new FakeAsignacionRepo(
                new AsignacionSemanal(Guid.NewGuid(), sesion.Id, SemanaAcademica.A, Guid.NewGuid(), null, Modalidad.Presencial));
            var service = new GrupoService(grupoRepo, new SesionCascadeService(sesionRepo, asignacionRepo), new FakeUnitOfWork());

            await service.DeleteAsync(grupo.Id);

            Assert.Null(await grupoRepo.GetByIdAsync(grupo.Id));
            Assert.Empty(await sesionRepo.GetAllAsync());
            Assert.Empty(await asignacionRepo.GetAllAsync());
        }
    }
}
