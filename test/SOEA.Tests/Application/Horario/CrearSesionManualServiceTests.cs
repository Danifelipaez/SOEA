using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Cubre la creación manual de sesión para los 3 tipos (laboratorio con alternancia,
    /// teoría virtual fija, teoría presencial). TeoriaVirtual_UnaFilaVirtualSinEspacio es guarda de
    /// regresión del fix de ModalidadSemanal: sin él la fila quedaba presencial y con espacio.
    /// </summary>
    public class CrearSesionManualServiceTests
    {
        private static readonly Guid Lab = Guid.NewGuid();
        private static readonly Guid Salon = Guid.NewGuid();

        private static (CrearSesionManualService svc, SOEA.Domain.Entities.Horario horario) Crear()
        {
            // Un Horario exige ≥1 sesión: un id sin fila basta, no participa en ningún chequeo.
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { Guid.NewGuid() });
            var svc = new CrearSesionManualService(
                new FakeBloqueRepo(GrillaInstitucional.GenerarBloques().ToArray()),
                new FakeHorarioRepo(horario), new FakeSesionRepo(), new FakeAsignacionRepo(),
                new FakeAsignaturaRepo(), new FakeGrupoRepo(),
                new FakeEspacioRepo(
                    new Espacio(Lab, "Lab 1", TipoEspacio.Laboratorio, 30),
                    new Espacio(Salon, "Salón 1", TipoEspacio.Salon, 30)),
                new FakeUnitOfWork());
            return (svc, horario);
        }

        [Fact]
        public async Task Laboratorio_ConAlternanciaTipoA_GeneraUnaFilaPresencialEnSemanaA()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Guid.NewGuid(),
                DocenteId = Guid.NewGuid(),
                EspacioId = Lab,
                Dia = "lunes",
                HoraInicio = "07:00",
                DuracionHoras = 2m,
                Alternancia = "TipoA",
                TipoFlujo = "Laboratorio"
            });

            // Una sola fila persistida: la presencial de la semana A. La contraparte virtual de
            // la semana B no reserva aula, así que la deriva la grilla, no el servicio.
            var a = Assert.Single(resultado);
            Assert.Equal("A", a.Semana);
            Assert.False(a.Virtual);
            Assert.Equal(Lab.ToString(), a.EspacioId);
        }

        [Fact]
        public async Task TeoriaVirtual_UnaFilaVirtualSinEspacio()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Guid.NewGuid(),
                DocenteId = Guid.NewGuid(),
                EspacioId = null,
                Dia = "martes",
                HoraInicio = "08:00",
                DuracionHoras = 2m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            });

            var unica = Assert.Single(resultado);
            Assert.True(unica.Virtual);
            Assert.Null(unica.EspacioId);
        }

        [Fact]
        public async Task TeoriaPresencial_IgnoraAlternanciaDelRequest()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Guid.NewGuid(),
                DocenteId = Guid.NewGuid(),
                EspacioId = Salon,
                Dia = "miercoles",
                HoraInicio = "09:00",
                DuracionHoras = 2m,
                Alternancia = "TipoA", // decisión: teoría nunca alterna — se ignora
                TipoFlujo = "AulaVirtual",
                EsVirtual = false
            });

            var unica = Assert.Single(resultado);
            Assert.Equal("SinAlternancia", unica.Alternancia);
            Assert.False(unica.Virtual);
            Assert.Equal(Salon.ToString(), unica.EspacioId);
        }

        /// <summary>P0-5 auditoría: antes la sesión no entraba en ningún horario y desaparecía al recargar.</summary>
        [Fact]
        public async Task SesionCreada_QuedaEnElHorarioVigente()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Guid.NewGuid(),
                Dia = "jueves",
                HoraInicio = "10:00",
                DuracionHoras = 2m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            });

            Assert.Contains(Guid.Parse(Assert.Single(resultado).Id), horario.SesioneIds);
        }

        [Fact]
        public async Task HorarioInexistente_LanzaKeyNotFound()
        {
            var (svc, _) = Crear();

            await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = Guid.NewGuid(),
                AsignaturaId = Guid.NewGuid(),
                Dia = "jueves",
                HoraInicio = "10:00",
                DuracionHoras = 2m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            }));
        }
    }
}
