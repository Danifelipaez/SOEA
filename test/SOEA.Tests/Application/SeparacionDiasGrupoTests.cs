using System;
using System.Threading.Tasks;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Grupos;
using SOEA.Application.Features.Sesiones;
using SOEA.Domain.Entities;
using SOEA.Tests.Fakes;
using Xunit;

namespace SOEA.Tests.Application
{
    /// <summary>
    /// HC-SEP en la captura: con n ≥ 2 sesiones del mismo tipo por semana, el grupo necesita n días
    /// disponibles con uno libre de por medio. Antes solo se descubría al generar, sin explicación.
    /// </summary>
    public class SeparacionDiasGrupoTests
    {
        private static string Disp(params string[] disponibles)
        {
            var dias = new[] { "lunes", "martes", "miercoles", "jueves", "viernes", "sabado" };
            return "{" + string.Join(",", Array.ConvertAll(dias, d =>
                $"\"{d}\":{{\"noDisponible\":{(Array.IndexOf(disponibles, d) < 0 ? "true" : "false")}}}")) + "}";
        }

        [Fact]
        public void UnaSesion_OSinDisponibilidad_NoExigeNada()
        {
            Assert.Null(GrupoService.ErrorDiasSeparados(Disp("lunes"), 1, "G1"));
            Assert.Null(GrupoService.ErrorDiasSeparados(null, 2, "G1"));
        }

        [Fact]
        public void DosSesiones_UnSoloDia_OConsecutivos_Error_ConDiaDePorMedio_Ok()
        {
            Assert.Contains("'G1'", GrupoService.ErrorDiasSeparados(Disp("lunes"), 2, "G1"));
            Assert.NotNull(GrupoService.ErrorDiasSeparados(Disp("lunes", "martes"), 2, "G1"));
            Assert.Null(GrupoService.ErrorDiasSeparados(Disp("lunes", "miercoles"), 2, "G1"));
        }

        [Fact]
        public void DiaSinEntrada_CuentaComoNoDisponible_IgualQueAlGenerar()
        {
            // El import de Excel solo declara los días con filas; los demás quedan cerrados.
            Assert.NotNull(GrupoService.ErrorDiasSeparados("{\"lunes\":{\"noDisponible\":false}}", 3, "G1"));
        }

        [Fact]
        public void CuatroSesiones_SiempreError()
        {
            Assert.Contains("como máximo 3", GrupoService.ErrorDiasSeparados(null, 4, "G1"));
        }

        private static (AsignaturaService servicio, Asignatura asig) Servicio(string disponibilidadGrupo)
        {
            var progId = Guid.NewGuid();
            var asig = new Asignatura(Guid.NewGuid(), "Cálculo", "CAL1", 2, 1, 0, progId); // 1 sesión/semana
            var grupo = new Grupo(Guid.NewGuid(), "G1", asig.Id, 30);
            grupo.ActualizarDisponibilidadUi(disponibilidadGrupo);
            var servicio = new AsignaturaService(new FakeAsignaturaRepo(asig), new FakeGrupoRepo(grupo),
                new SesionCascadeService(new FakeSesionRepo(), new FakeAsignacionRepo()), new FakeUnitOfWork());
            return (servicio, asig);
        }

        private static UpdateAsignaturaRequest Request(Asignatura a, int sesiones) => new()
        {
            Nombre = a.Nombre, Codigo = a.Codigo, ProgramaId = a.ProgramaId,
            SesionesTeoriaPresencialSemana = sesiones, HorasTeoriaPresencial = 2
        };

        [Fact]
        public async Task SubirSesiones_ConGrupoDeUnSoloDia_Rechaza()
        {
            var (servicio, asig) = Servicio(Disp("lunes"));
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => servicio.UpdateAsync(asig.Id, Request(asig, 2)));
            Assert.Contains("'G1'", ex.Message);
        }

        [Fact]
        public async Task SubirSesiones_ConGrupoConDiasSeparados_Permite()
        {
            var (servicio, asig) = Servicio(Disp("lunes", "miercoles"));
            var r = await servicio.UpdateAsync(asig.Id, Request(asig, 2));
            Assert.Equal(2, r.SesionesTeoriaPresencialSemana);
        }
    }
}
