using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Application.Features.Grupos;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Entities;
using SOEA.Domain.ValueObjects;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// Crear/editar grupo vivía en GruposController (regla 5 de CLAUDE.md); ahora en GrupoService.
    /// Fija las reglas que se movieron: asignatura obligatoria y existente, y jerarquía derivada.
    /// </summary>
    public class GrupoServiceCrudTests
    {
        private static readonly Facultad Facultad = new(Guid.NewGuid(), "Ingeniería");
        private static readonly Programa Programa = new(Guid.NewGuid(), "Sistemas", Facultad.Id);
        private static readonly Asignatura Asignatura = new(Guid.NewGuid(), "Cálculo", "C1", 1, 2, 0, 0, 0, 0, 0, Programa.Id);

        private static (GrupoService service, FakeGrupoRepo repo) Crear(params Grupo[] grupos)
        {
            var repo = new FakeGrupoRepo(grupos);
            var service = new GrupoService(repo, new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()),
                new FakeUnitOfWork(), new FakeAsignaturaRepo(Asignatura), new FakeProgramaRepo(Programa));
            return (service, repo);
        }

        private static GrupoDatos Datos(Guid? asignaturaId) =>
            new(asignaturaId, "G1", null, 30, null, null, new List<RequisitoEspacio>());

        [Fact]
        public async Task Crear_GuardaElGrupoYDevuelveSuProgramaYFacultad()
        {
            var (service, repo) = Crear();

            var creado = await service.CrearAsync(Guid.Empty, Datos(Asignatura.Id));

            Assert.NotEqual(Guid.Empty, creado.Grupo.Id);
            Assert.Equal(Programa.Id, creado.ProgramaId);
            Assert.Equal(Facultad.Id, creado.FacultadId);
            Assert.NotNull(await repo.GetByIdAsync(creado.Grupo.Id));
        }

        [Fact]
        public async Task Crear_SinAsignatura_EsArgumentException()
        {
            var (service, _) = Crear();
            await Assert.ThrowsAsync<ArgumentException>(() => service.CrearAsync(Guid.Empty, Datos(null)));
        }

        [Fact]
        public async Task Crear_ConAsignaturaInexistente_EsArgumentException()
        {
            var (service, _) = Crear();
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CrearAsync(Guid.Empty, Datos(Guid.NewGuid())));
            Assert.Contains("No existe la asignatura", ex.Message);
        }

        [Fact]
        public async Task Actualizar_GrupoInexistente_EsKeyNotFound()
        {
            var (service, _) = Crear();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ActualizarAsync(Guid.NewGuid(), Datos(Asignatura.Id)));
        }

        [Fact]
        public async Task Actualizar_CambiaLosDatosDelGrupo()
        {
            var existente = new Grupo(Guid.NewGuid(), "Viejo", Asignatura.Id, 10);
            var (service, repo) = Crear(existente);

            await service.ActualizarAsync(existente.Id, Datos(Asignatura.Id) with { Nombre = "Nuevo", EstudiantesInscritos = 40 });

            var guardado = await repo.GetByIdAsync(existente.Id);
            Assert.Equal("Nuevo", guardado!.Nombre);
            Assert.Equal(40, guardado.EstudiantesInscritos);
        }
    }
}
