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
    /// MapearSesionesInicialesTests.GrupoCuyaAsignaturaNoResuelve_SeDescartaYQuedaAdvertenciaConSuNombre
    /// ya fija que la advertencia nombra al grupo por Nombre — pero Nombre no es único (se
    /// encontraron 3 grupos "G1" y 2 "G2" en la BD de desarrollo, todos huérfanos por asignaturas
    /// borradas). Sin el Id, es imposible saber cuál de los homónimos es el problemático. Estas
    /// pruebas cubren que el Id ahora viaja en el mismo mensaje, sin tocar la advertencia existente.
    /// </summary>
    public class MapearSesionesInicialesAdvertenciaIdTests
    {
        [Fact]
        public void GrupoHuerfano_LaAdvertenciaIncluyeSuId()
        {
            var grupo = new Grupo(Guid.NewGuid(), "Grupo Huérfano", Guid.Empty, 30,
                asignaturaId: Guid.NewGuid());

            var (_, advertencias) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { grupo }, new List<AsignaturaDto>());

            Assert.Contains(advertencias, a => a.Contains(grupo.Id.ToString()));
        }

        [Fact]
        public void DosGruposHuerfanosMismoNombre_CadaAdvertenciaEsDistinguiblePorId()
        {
            var g1 = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: Guid.NewGuid());
            var g2 = new Grupo(Guid.NewGuid(), "G1", Guid.Empty, 30, asignaturaId: Guid.NewGuid());

            var (_, advertencias) = GenerarHorarioService.MapearSesionesIniciales(
                new List<Grupo> { g1, g2 }, new List<AsignaturaDto>());

            Assert.Equal(2, advertencias.Count);
            Assert.Contains(advertencias, a => a.Contains(g1.Id.ToString()));
            Assert.Contains(advertencias, a => a.Contains(g2.Id.ToString()));
        }
    }
}
