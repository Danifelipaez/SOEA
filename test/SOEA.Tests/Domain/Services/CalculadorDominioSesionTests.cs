using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Tests.Domain.Services
{
    /// <summary>
    /// StartsPermitidos re-implementaba el filtro de disponibilidad de grupo por fuera de
    /// BloquesPlanner.StartsValidos, comprobando solo el bloque de INICIO en vez del span
    /// completo — BloquesPlanner ya soporta esto vía su parámetro bloquesDisponibles
    /// (ver BloquesPlannerTests.StartsValidos_FiltraPorDisponibilidadYNoCrucesDeDia).
    /// </summary>
    public class CalculadorDominioSesionTests
    {
        private static List<BloqueTiempo> Bloques5() =>
            Enumerable.Range(0, 5)
                .Select(h => new BloqueTiempo(System.Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(7 + h, 0), new TimeOnly(8 + h, 0)))
                .ToList();

        [Fact]
        public void StartsPermitidos_SesionMultibloque_ExigeQueTodoElSpanEsteEnLaDisponibilidad()
        {
            var bloques = Bloques5();
            var rangos = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            // Grupo solo disponible en los bloques 0,1,2 — una sesión de 2h que empiece en el
            // bloque 2 necesitaría también el bloque 3, que NO está permitido.
            var permitidosGrupo = new HashSet<int> { 0, 1, 2 };

            var starts = CalculadorDominioSesion.StartsPermitidos(
                duracion: 2, bloques, rangos, diaPorIdx, permitidosGrupo);

            Assert.Equal(new[] { 0, 1 }, starts);
        }
    }
}
