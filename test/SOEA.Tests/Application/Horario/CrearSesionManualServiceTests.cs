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
        private static readonly Guid Asig = Guid.NewGuid();
        private static readonly Guid Grupo = Guid.NewGuid();

        private static (CrearSesionManualService svc, SOEA.Domain.Entities.Horario horario) Crear()
        {
            // Un Horario exige ≥1 sesión: un id sin fila basta, no participa en ningún chequeo.
            var horario = new SOEA.Domain.Entities.Horario(Guid.NewGuid(), "2026-1", new List<Guid> { Guid.NewGuid() });
            var svc = new CrearSesionManualService(
                new FakeBloqueRepo(GrillaInstitucional.GenerarBloques().ToArray()),
                new FakeHorarioRepo(horario), new FakeSesionRepo(), new FakeAsignacionRepo(),
                new FakeAsignaturaRepo(), new FakeGrupoRepo(new Grupo(Grupo, "G1", Asig, 30)),
                new FakeEspacioRepo(
                    new Espacio(Lab, "Lab 1", TipoEspacio.Laboratorio, 30),
                    new Espacio(Salon, "Salón 1", TipoEspacio.Salon, 30)),
                new FakeUnitOfWork());
            return (svc, horario);
        }

        [Fact]
        public async Task Laboratorio_NuncaAlterna_UnaFilaPresencialParaTodasLasSemanas()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Asig,
                GrupoId = Grupo,
                DocenteId = Guid.NewGuid(),
                EspacioId = Lab,
                Dia = "lunes",
                HoraInicio = "07:00",
                DuracionHoras = 2m,
                TipoFlujo = "Laboratorio"
            });

            // Regla 10: solo el generador empareja. La sesión manual ocupa su aula todas las semanas
            // (semana vacía en el DTO) y no deja una "Semana B" suelta que la grilla no mostraría.
            var a = Assert.Single(resultado);
            Assert.Equal("SinAlternancia", a.Alternancia);
            Assert.Equal("", a.Semana);
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
                AsignaturaId = Asig,
                GrupoId = Grupo,
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
        public async Task TeoriaPresencial_NuncaAlterna()
        {
            var (svc, horario) = Crear();

            var resultado = await svc.EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = Asig,
                GrupoId = Grupo,
                DocenteId = Guid.NewGuid(),
                EspacioId = Salon,
                Dia = "miercoles",
                HoraInicio = "09:00",
                DuracionHoras = 2m,
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
                AsignaturaId = Asig,
                GrupoId = Grupo,
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
                AsignaturaId = Asig,
                GrupoId = Grupo,
                Dia = "jueves",
                HoraInicio = "10:00",
                DuracionHoras = 2m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            }));
        }

        [Fact]
        public async Task SinGrupo_OGrupoDeOtraAsignatura_Lanza()
        {
            // Jerarquía Sesión → Grupo → Asignatura: sin grupo no hay sesión, y el grupo debe ser de esa asignatura.
            var (svc, horario) = Crear();
            CrearSesionManualRequest Req(Guid asig, Guid grupo) => new()
            {
                HorarioId = horario.Id, AsignaturaId = asig, GrupoId = grupo,
                Dia = "lunes", HoraInicio = "07:00", DuracionHoras = 2m, TipoFlujo = "AulaVirtual", EsVirtual = true
            };

            await Assert.ThrowsAsync<ArgumentException>(() => svc.EjecutarAsync(Req(Asig, Guid.Empty)));
            await Assert.ThrowsAsync<ArgumentException>(() => svc.EjecutarAsync(Req(Guid.NewGuid(), Grupo)));
            await Assert.ThrowsAsync<SOEA.Domain.Exceptions.BusinessRuleViolationException>(() => svc.EjecutarAsync(Req(Asig, Guid.NewGuid())));
        }
    }
}
