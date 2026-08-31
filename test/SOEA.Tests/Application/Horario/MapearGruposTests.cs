using System.Collections.Generic;
using System.Linq;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;
using Xunit;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// G1/G3 (bugs reportados): <see cref="GenerarHorarioService.MapearGrupos"/> tiraba el
    /// <c>DocenteId</c> del grupo (nunca llegaba a las sesiones generadas, el docente parecía
    /// "no importarse") y descartaba en silencio cualquier grupo con Id inválido (menos grupos
    /// en el horario de los que existen, sin ninguna pista de por qué).
    /// </summary>
    public class MapearGruposTests
    {
        private static GrupoDto DtoValido(string? docenteId = null) => new()
        {
            Id = System.Guid.NewGuid().ToString(),
            Nombre = "G1",
            AsignaturaId = System.Guid.NewGuid().ToString(),
            EstudiantesInscritos = 30,
            DocenteId = docenteId
        };

        [Fact]
        public void GrupoConDocenteId_SeMapeaConDocenteEnLaEntidad()
        {
            var docenteId = System.Guid.NewGuid();
            var dto = DtoValido(docenteId.ToString());

            var (grupos, advertencias) = GenerarHorarioService.MapearGrupos(new List<GrupoDto> { dto });

            var grupo = Assert.Single(grupos);
            Assert.Equal(docenteId, grupo.DocenteId);
            Assert.Empty(advertencias);
        }

        [Fact]
        public void GrupoSinDocenteId_MapeaDocenteIdNull_NoRevienta()
        {
            var dto = DtoValido(docenteId: null);

            var (grupos, _) = GenerarHorarioService.MapearGrupos(new List<GrupoDto> { dto });

            Assert.Null(Assert.Single(grupos).DocenteId);
        }

        [Fact]
        public void GrupoConIdInvalido_SeDescartaYQuedaAdvertenciaConSuNombre()
        {
            var dto = DtoValido();
            dto.Id = "no-es-un-guid";
            dto.Nombre = "Grupo Fantasma";

            var (grupos, advertencias) = GenerarHorarioService.MapearGrupos(new List<GrupoDto> { dto });

            Assert.Empty(grupos);
            Assert.Contains(advertencias, a => a.Contains("Grupo Fantasma"));
            // El bug era el descarte silencioso: la advertencia no debe volver a esconder el
            // problema detrás de un identificador que el usuario no puede leer.
            Assert.DoesNotContain(advertencias, a => a.Contains(dto.Id));
        }

        [Fact]
        public void DosGruposValidos_AmbosSeMapean()
        {
            var dtos = new List<GrupoDto> { DtoValido(), DtoValido() };

            var (grupos, advertencias) = GenerarHorarioService.MapearGrupos(dtos);

            Assert.Equal(2, grupos.Count);
            Assert.Empty(advertencias);
        }
    }
}
