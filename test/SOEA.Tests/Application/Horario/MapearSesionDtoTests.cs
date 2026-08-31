using System;
using System.Collections.Generic;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// G3 (bug reportado): <see cref="GenerarHorarioService.MapearSesionDto"/> no emitía
    /// <c>GrupoId</c> en el DTO de respuesta. Sin él, el frontend no puede distinguir dos grupos
    /// de la misma asignatura — ambos se pintaban como una sola fila en el horario.
    /// </summary>
    public class MapearSesionDtoTests
    {
        [Fact]
        public void EmiteElGrupoIdDeLaSesion()
        {
            var grupoId = Guid.NewGuid();
            var sesion = new Sesion(
                id: Guid.NewGuid(), asignaturaId: Guid.NewGuid(), docenteId: null,
                bloqueId: Guid.NewGuid(), espacioId: null, grupoId: grupoId,
                alternancia: TipoAlternancia.SinAlternancia, modalidad: Modalidad.Presencial,
                duracionHoras: 2m, esBloque: false, estaDividida: false);
            var asignacion = new AsignacionSemanal(
                id: Guid.NewGuid(), sesionId: sesion.Id, semana: SemanaAcademica.A,
                bloqueTiempoId: Guid.NewGuid(), espacioId: null, modalidad: Modalidad.Presencial);

            var dto = GenerarHorarioService.MapearSesionDto(
                asignacion, sesion, new Dictionary<Guid, BloqueTiempo>(), espacioIdHogar: null);

            Assert.Equal(grupoId.ToString(), dto.GrupoId);
        }

        [Fact]
        public void SesionSinGrupo_EmiteGrupoIdVacio_NoRevienta()
        {
            var sesion = new Sesion(
                id: Guid.NewGuid(), asignaturaId: Guid.NewGuid(), docenteId: null,
                bloqueId: Guid.NewGuid(), espacioId: null, grupoId: null,
                alternancia: TipoAlternancia.SinAlternancia, modalidad: Modalidad.Presencial,
                duracionHoras: 2m, esBloque: false, estaDividida: false);
            var asignacion = new AsignacionSemanal(
                id: Guid.NewGuid(), sesionId: sesion.Id, semana: SemanaAcademica.A,
                bloqueTiempoId: Guid.NewGuid(), espacioId: null, modalidad: Modalidad.Presencial);

            var dto = GenerarHorarioService.MapearSesionDto(
                asignacion, sesion, new Dictionary<Guid, BloqueTiempo>(), espacioIdHogar: null);

            Assert.Equal(string.Empty, dto.GrupoId);
        }
    }
}
