using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using SOEA.Domain.Entities;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// G1/G3 (bugs reportados): <see cref="GenerarHorarioService.MapearSesionesIniciales"/>
    /// construía cada sesión con <c>docenteId: null</c> fijo — el docente del grupo (persistido y
    /// correcto en BD) nunca llegaba a la sesión generada, así que la grilla mostraba "sin
    /// docente" siempre. Y un grupo cuya asignatura no resolvía se descartaba en silencio,
    /// sin que el usuario supiera cuál faltaba ni por qué.
    /// </summary>
    public class MapearSesionesInicialesTests
    {
        private static AsignaturaDto AsignaturaConUnaTeoriaPresencial(Guid id) => new()
        {
            Id = id.ToString(),
            Nombre = "Cálculo I",
            SesionesTeoriaPresencialSemana = 1,
            HorasTeoriaPresencial = 2,
            SesionesTeoriaVirtualSemana = 0,
            HorasTeoriaVirtual = 2,
            SesionesLaboratorioSemana = 0,
            HorasLaboratorio = 2
        };

        [Fact]
        public void GrupoConDocente_SiembraElDocenteEnCadaSesionCreada()
        {
            var asigId = Guid.NewGuid();
            var docenteId = Guid.NewGuid();
            var grupo = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: asigId, docenteId: docenteId);

            var (sesiones, advertencias) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { grupo }, new List<AsignaturaDto> { AsignaturaConUnaTeoriaPresencial(asigId) });

            var sesion = Assert.Single(sesiones);
            Assert.Equal(docenteId, sesion.DocenteId);
            Assert.Empty(advertencias);
        }

        [Fact]
        public void GrupoSinDocente_SesionQuedaSinDocente_NoRevienta()
        {
            var asigId = Guid.NewGuid();
            var grupo = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: asigId, docenteId: null);

            var (sesiones, _) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { grupo }, new List<AsignaturaDto> { AsignaturaConUnaTeoriaPresencial(asigId) });

            Assert.Null(Assert.Single(sesiones).DocenteId);
        }

        [Fact]
        public void GrupoCuyaAsignaturaNoResuelve_SeDescartaYQuedaAdvertenciaConSuNombre()
        {
            var grupo = new Grupo(Guid.NewGuid(), "Grupo Huérfano", Guid.Empty, 30,
                asignaturaId: Guid.NewGuid()); // no está en la lista de asignaturas del request

            var (sesiones, advertencias) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { grupo }, new List<AsignaturaDto>());

            Assert.Empty(sesiones);
            Assert.Contains(advertencias, a => a.Contains("Grupo Huérfano"));
        }

        [Fact]
        public void DosGruposDeLaMismaAsignatura_CadaUnoConservaSuPropioGrupoId()
        {
            var asigId = Guid.NewGuid();
            var g1 = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: asigId);
            var g2 = new Grupo(Guid.NewGuid(), "G2", Guid.Empty, 30, asignaturaId: asigId);

            var (sesiones, _) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { g1, g2 }, new List<AsignaturaDto> { AsignaturaConUnaTeoriaPresencial(asigId) });

            Assert.Equal(2, sesiones.Count);
            Assert.Contains(sesiones, s => s.GrupoId == g1.Id);
            Assert.Contains(sesiones, s => s.GrupoId == g2.Id);
        }
    }
}
