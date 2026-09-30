using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SOEA.API.Controllers;
using SOEA.Application.Features.Espacios;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Enums;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Api
{
    /// <summary>
    /// L-12 (auditoría 2026-09-28): <c>POST /espacios</c> con <c>"Salon"</c> (sin tilde, el literal que usa el resto del
    /// contrato) respondía 400 porque solo aceptaba <c>"Salón"</c> exacto.
    /// </summary>
    public class EspaciosControllerTipoTests
    {
        private static (EspaciosController controller, FakeEspacioRepo repo) Crear()
        {
            var repo = new FakeEspacioRepo();
            var servicio = new EspacioService(repo, new FakeGrupoRepo(),
                new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());
            return (new EspaciosController(repo, servicio), repo);
        }

        [Theory]
        [InlineData("Salón", TipoEspacio.Salon)]
        [InlineData("Salon", TipoEspacio.Salon)]
        [InlineData("salon", TipoEspacio.Salon)]
        [InlineData(" LABORATORIO ", TipoEspacio.Laboratorio)]
        [InlineData("auditorio", TipoEspacio.Auditorio)]
        public async Task Crear_AceptaElTipoConOSinTildeYEnCualquierCapitalizacion(string tipo, TipoEspacio esperado)
        {
            var (controller, repo) = Crear();

            var r = await controller.Create(new EspacioDto { Nombre = "Aula", Tipo = tipo, Capacidad = 30 });

            Assert.Equal(StatusCodes201, ((ObjectResult)r.Result!).StatusCode);
            Assert.Equal(esperado, Assert.Single(await repo.GetAllAsync()).Tipo);
        }

        [Fact]
        public async Task Crear_ConUnTipoDesconocido_SigueSiendoArgumentException_ConMensajeClaro()
        {
            var (controller, _) = Crear();

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                controller.Create(new EspacioDto { Nombre = "Aula", Tipo = "Cancha", Capacidad = 30 }));

            Assert.Contains("Valores válidos", ex.Message);
        }

        private const int StatusCodes201 = 201;
    }
}
