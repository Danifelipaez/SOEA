using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Horario.Requests;

namespace SOEA.Tests.Application.Horario
{
    /// <summary>
    /// B4 auditoría: <see cref="GenerarHorarioService.MapearConfiguracion"/> descartaba
    /// PesoBalanceSemanas, PesoPresencialFirst y Semilla del DTO — quedaban siempre en el default
    /// del motor aunque el frontend los enviara, y la ejecución en producción era irreproducible.
    /// </summary>
    public class MapearConfiguracionTests
    {
        [Fact]
        public void DtoNull_UsaDefaultsDelMotor()
        {
            var cfg = GenerarHorarioService.MapearConfiguracion(null);

            Assert.Equal(50, cfg.TamañoPoblacion);
            Assert.Equal(200, cfg.MaxGeneraciones);
            Assert.Null(cfg.Semilla);
        }

        [Fact]
        public void DtoCompleto_MapeaTodosLosCampos_IncluidosLosQueAntesSeDescartaban()
        {
            var dto = new ConfiguracionAlgoritmoDto
            {
                TamañoPoblacion      = 30,
                MaxGeneraciones      = 100,
                ProbabilidadMutacion = 0.1,
                ProbabilidadCruce    = 0.7,
                UmbralConvergencia   = 15,
                PesoErgo             = 5,
                PesoTiempos          = 4,
                PesoMaxHorasSeguidas = 6,
                PesoBalanceSemanas   = 7,
                PesoPresencialFirst  = 8,
                Semilla              = 12345
            };

            var cfg = GenerarHorarioService.MapearConfiguracion(dto);

            Assert.Equal(30, cfg.TamañoPoblacion);
            Assert.Equal(100, cfg.MaxGeneraciones);
            Assert.Equal(0.1, cfg.ProbabilidadMutacion);
            Assert.Equal(0.7, cfg.ProbabilidadCruce);
            Assert.Equal(15, cfg.UmbralConvergencia);
            Assert.Equal(5, cfg.PesoErgo);
            Assert.Equal(4, cfg.PesoTiempos);
            Assert.Equal(6, cfg.PesoMaxHorasSeguidas);
            Assert.Equal(7, cfg.PesoBalanceSemanas);   // antes se perdía en el mapeo
            Assert.Equal(8, cfg.PesoPresencialFirst);  // antes se perdía en el mapeo
            Assert.Equal(12345, cfg.Semilla);          // antes se perdía en el mapeo (irreproducible)
        }

        // SEC-2 auditoría 2026-09-28: sin tope superior, MaxGeneraciones = 2e9 dejaba la CPU ocupada
        // mientras el cliente no colgara.
        public static IEnumerable<object[]> FueraDeRango() => new[]
        {
            new object[] { new ConfiguracionAlgoritmoDto { MaxGeneraciones = 2_000_000_000 } },
            new object[] { new ConfiguracionAlgoritmoDto { MaxGeneraciones = 0 } },
            new object[] { new ConfiguracionAlgoritmoDto { UmbralConvergencia = 2_000_000_000 } },
            new object[] { new ConfiguracionAlgoritmoDto { TamañoPoblacion = 2_000_000_000 } },
            new object[] { new ConfiguracionAlgoritmoDto { TamañoPoblacion = 9 } },
            new object[] { new ConfiguracionAlgoritmoDto { ProbabilidadMutacion = 1.5 } },
            new object[] { new ConfiguracionAlgoritmoDto { ProbabilidadCruce = double.NaN } },
            new object[] { new ConfiguracionAlgoritmoDto { PesoErgo = -1 } },
            new object[] { new ConfiguracionAlgoritmoDto { PesoPresencialFirst = int.MaxValue } },
        };

        [Theory]
        [MemberData(nameof(FueraDeRango))]
        public void ValorFueraDeRango_Lanza_ArgumentException(ConfiguracionAlgoritmoDto dto)
        {
            var ex = Assert.Throws<ArgumentException>(() => GenerarHorarioService.MapearConfiguracion(dto));
            Assert.Contains("debe estar entre", ex.Message);
        }

        [Fact]
        public void LosTopes_AdmitenExactamenteElLimite()
        {
            var cfg = GenerarHorarioService.MapearConfiguracion(new ConfiguracionAlgoritmoDto
            {
                TamañoPoblacion = 200, MaxGeneraciones = 1000, UmbralConvergencia = 1000,
                ProbabilidadMutacion = 1, ProbabilidadCruce = 0, PesoErgo = 100
            });

            Assert.Equal(200, cfg.TamañoPoblacion);
            Assert.Equal(1000, cfg.MaxGeneraciones);
        }
    }
}
