using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Services;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// Los 409 inducidos por el usuario al crear una sesión a mano deben identificar la sesión con
    /// la que chocan, en español y con sugerencia — y chocar solo con lo que de verdad ocupa esa
    /// franja en el horario vigente.
    /// </summary>
    public class CrearSesionManualServiceMensajesTests
    {
        private static readonly Guid AsigExistente = Guid.NewGuid();
        private static readonly Guid AsigNueva = Guid.NewGuid();

        private static BloqueTiempo Bloque(DiaDeSemana dia, int hora) =>
            GrillaInstitucional.GenerarBloques().First(b => b.Dia == dia && b.HoraInicio == new TimeOnly(hora, 0));

        private static SOEA.Domain.Entities.Horario HorarioCon(params Guid[] sesionIds) =>
            new(Guid.NewGuid(), "2026-1", sesionIds.Length > 0 ? sesionIds.ToList() : new List<Guid> { Guid.NewGuid() });

        private static CrearSesionManualService Crear(
            SOEA.Domain.Entities.Horario horario, Sesion existente, AsignacionSemanal asignacion, params Espacio[] espacios) =>
            new(new FakeBloqueRepo(GrillaInstitucional.GenerarBloques().ToArray()),
                new FakeHorarioRepo(horario),
                new FakeSesionRepo(existente),
                new FakeAsignacionRepo(asignacion),
                new FakeAsignaturaRepo(
                    new Asignatura(AsigExistente, "Física I", "COD-FIS", 1, 1, 0, Guid.NewGuid()),
                    new Asignatura(AsigNueva, "Cálculo I", "COD-CALC", 1, 1, 0, Guid.NewGuid())),
                new FakeGrupoRepo(),
                new FakeEspacioRepo(espacios),
                new FakeUnitOfWork());

        [Fact]
        public async Task HCI01_DocenteYaOcupado_MensajeIdentificaAmbasAsignaturasConDiaYHora()
        {
            var bloque = Bloque(DiaDeSemana.Lunes, 7);
            var docenteId = Guid.NewGuid();
            var existente = new Sesion(Guid.NewGuid(), AsigExistente, docenteId, bloque.Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);
            var asignacion = new AsignacionSemanal(Guid.NewGuid(), existente.Id, SemanaAcademica.A, bloque.Id, null, Modalidad.Virtual);
            var horario = HorarioCon(existente.Id);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                Crear(horario, existente, asignacion).EjecutarAsync(new CrearSesionManualRequest
                {
                    HorarioId = horario.Id,
                    AsignaturaId = AsigNueva,
                    DocenteId = docenteId,
                    Dia = "lunes",
                    HoraInicio = "07:00",
                    DuracionHoras = 1m,
                    TipoFlujo = "AulaVirtual",
                    EsVirtual = true
                }));

            Assert.Contains("HC-I01", ex.Message);
            Assert.Contains("Sesión 1", ex.Message);
            Assert.Contains("Sesión 2", ex.Message);
            Assert.Contains("Cálculo I", ex.Message);
            Assert.Contains("Física I", ex.Message);
            Assert.Contains("lunes", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("07:00", ex.Message);
            Assert.Contains("Elija una hora diferente", ex.Message);
        }

        [Fact]
        public async Task HCS01_EspacioYaOcupado_MensajeIdentificaAmbasAsignaturas()
        {
            var bloque = Bloque(DiaDeSemana.Martes, 8);
            var lab = new Espacio(Guid.NewGuid(), "Lab 1", TipoEspacio.Laboratorio, 30);
            var existente = new Sesion(Guid.NewGuid(), AsigExistente, Guid.NewGuid(), bloque.Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 1m, false, false);
            var asignacion = new AsignacionSemanal(Guid.NewGuid(), existente.Id, SemanaAcademica.A, bloque.Id, lab.Id, Modalidad.Presencial);
            var horario = HorarioCon(existente.Id);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                Crear(horario, existente, asignacion, lab).EjecutarAsync(new CrearSesionManualRequest
                {
                    HorarioId = horario.Id,
                    AsignaturaId = AsigNueva,
                    EspacioId = lab.Id,
                    Dia = "martes",
                    HoraInicio = "08:00",
                    DuracionHoras = 1m,
                    TipoFlujo = "Laboratorio"
                }));

            Assert.Contains("HC-S01", ex.Message);
            Assert.Contains("Sesión 1", ex.Message);
            Assert.Contains("Sesión 2", ex.Message);
            Assert.Contains("Cálculo I", ex.Message);
            Assert.Contains("Física I", ex.Message);
            Assert.Contains("Asigne un espacio distinto", ex.Message);
        }

        /// <summary>
        /// P0-4 auditoría: el chequeo leía Sesion.BloqueTiempoId, la pista de Fase 1. Aquí la pista dice
        /// lunes 07:00 pero la asignación real es martes 08:00–10:00: una sesión nueva el martes a las
        /// 09:00 en la misma aula se solapa y debe rechazarse (antes respondía 201).
        /// </summary>
        [Fact]
        public async Task HCS01_ComparaContraLaAsignacionReal_NoContraElBloqueDeFase1()
        {
            var lab = new Espacio(Guid.NewGuid(), "Lab 1", TipoEspacio.Laboratorio, 30);
            var existente = new Sesion(Guid.NewGuid(), AsigExistente, null, Bloque(DiaDeSemana.Lunes, 7).Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Presencial, 2m, false, false);
            var asignacion = new AsignacionSemanal(Guid.NewGuid(), existente.Id, SemanaAcademica.A,
                Bloque(DiaDeSemana.Martes, 8).Id, lab.Id, Modalidad.Presencial);
            var horario = HorarioCon(existente.Id);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                Crear(horario, existente, asignacion, lab).EjecutarAsync(new CrearSesionManualRequest
                {
                    HorarioId = horario.Id,
                    AsignaturaId = AsigNueva,
                    EspacioId = lab.Id,
                    Dia = "martes",
                    HoraInicio = "09:00",
                    DuracionHoras = 2m,
                    TipoFlujo = "Laboratorio"
                }));

            Assert.Contains("HC-S01", ex.Message);
        }

        /// <summary>P0-5 auditoría: una sesión que no pertenece al horario vigente no se ve en ninguna pantalla y no debe bloquear.</summary>
        [Fact]
        public async Task SesionFueraDelHorarioVigente_NoBloqueaAlDocente()
        {
            var bloque = Bloque(DiaDeSemana.Lunes, 7);
            var docenteId = Guid.NewGuid();
            var fantasma = new Sesion(Guid.NewGuid(), AsigExistente, docenteId, bloque.Id, null, null,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, 1m, false, false);
            var asignacion = new AsignacionSemanal(Guid.NewGuid(), fantasma.Id, SemanaAcademica.A, bloque.Id, null, Modalidad.Virtual);
            var horario = HorarioCon(); // no contiene a la fantasma

            var resultado = await Crear(horario, fantasma, asignacion).EjecutarAsync(new CrearSesionManualRequest
            {
                HorarioId = horario.Id,
                AsignaturaId = AsigNueva,
                DocenteId = docenteId,
                Dia = "lunes",
                HoraInicio = "07:00",
                DuracionHoras = 1m,
                TipoFlujo = "AulaVirtual",
                EsVirtual = true
            });

            Assert.Single(resultado);
        }
    }
}
