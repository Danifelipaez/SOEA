using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// "Liberar espacio del horario": la clase sale del horario (sesión, asignación y lista del horario)
    /// y, si alterna, se va con su pareja — sola, la otra quedaría alternando sin nadie en su aula (regla 10).
    /// </summary>
    public class LiberarSesionServiceTests
    {
        private static Sesion NuevaSesion(Guid? pareja = null) => new(
            Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, Guid.NewGuid(),
            pareja is null ? TipoAlternancia.SinAlternancia : TipoAlternancia.TipoA, Modalidad.Presencial, 2m,
            esBloque: false, estaDividida: false, parejaAlternanciaId: pareja);

        private static AsignacionSemanal AsignacionDe(Sesion s) =>
            new(Guid.NewGuid(), s.Id, SemanaAcademica.A, s.BloqueTiempoId, null, Modalidad.Presencial);

        [Fact]
        public async Task LiberaSoloEsaClase_SinPareja()
        {
            var (a, b) = (NuevaSesion(), NuevaSesion());
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { a.Id, b.Id });
            var sesiones = new FakeSesionRepo(a, b);
            var asignaciones = new FakeAsignacionRepo(AsignacionDe(a), AsignacionDe(b));
            var uow = new FakeUnitOfWork();
            var svc = new LiberarSesionService(new FakeHorarioRepo(horario), sesiones, asignaciones, uow);

            var liberadas = await svc.EjecutarAsync(horario.Id, a.Id);

            Assert.Equal(new[] { a.Id }, liberadas);
            Assert.Equal(new[] { b.Id }, horario.SesioneIds);
            Assert.Equal(new[] { a.Id }, sesiones.Eliminados);
            Assert.Single(asignaciones.Eliminados);
            Assert.Equal(1, uow.Commits);
        }

        [Fact]
        public async Task ConPareja_LiberaLasDos()
        {
            var pareja = Guid.NewGuid();
            var (a, b, otra) = (NuevaSesion(pareja), NuevaSesion(pareja), NuevaSesion());
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { a.Id, b.Id, otra.Id });
            var svc = new LiberarSesionService(new FakeHorarioRepo(horario), new FakeSesionRepo(a, b, otra),
                new FakeAsignacionRepo(), new FakeUnitOfWork());

            var liberadas = await svc.EjecutarAsync(horario.Id, a.Id);

            Assert.Equal(new[] { a.Id, b.Id }, liberadas);
            Assert.Equal(new[] { otra.Id }, horario.SesioneIds);
        }

        [Fact]
        public async Task ClaseQueNoEstaEnElHorario_404()
        {
            var a = NuevaSesion();
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { a.Id });
            var svc = new LiberarSesionService(new FakeHorarioRepo(horario), new FakeSesionRepo(a),
                new FakeAsignacionRepo(), new FakeUnitOfWork());

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.EjecutarAsync(horario.Id, Guid.NewGuid()));
        }
    }
}
