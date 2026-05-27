using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Tests.Domain.Services
{
    public class BloquesPlannerTests
    {
        private static System.Collections.Generic.List<BloqueTiempo> Grilla()
        {
            var b = new System.Collections.Generic.List<BloqueTiempo>();
            // Lunes 7-22 (15 bloques)
            for (int h = 7; h < 22; h++)
                b.Add(new BloqueTiempo(System.Guid.NewGuid(), DiaDeSemana.Lunes, new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
            // Martes 7-13 (6 bloques)
            for (int h = 7; h < 13; h++)
                b.Add(new BloqueTiempo(System.Guid.NewGuid(), DiaDeSemana.Martes, new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
            return b;
        }

        [Fact]
        public void RangosPorDia_DevuelveLimitesCorrectos()
        {
            var bloques = Grilla();
            var rangos = BloquesPlanner.RangosPorDia(bloques);

            Assert.Equal((0, 14), rangos[DiaDeSemana.Lunes]);
            Assert.Equal((15, 20), rangos[DiaDeSemana.Martes]);
        }

        [Fact]
        public void CabeEnDia_SpanDe2HoraEnUltimoBloqueLunes_Falso()
        {
            var bloques = Grilla();
            var rangos = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // Bloque 14 = Lunes 21:00-22:00; un span de 2 desde 14 cruzaría a martes.
            Assert.False(BloquesPlanner.CabeEnDia(14, 2, rangos, diaPorIdx));
            // Bloque 13 = Lunes 20:00; un span de 2 sí cabe (20:00-22:00).
            Assert.True(BloquesPlanner.CabeEnDia(13, 2, rangos, diaPorIdx));
        }

        [Fact]
        public void StartsValidos_FiltraPorDisponibilidadYNoCrucesDeDia()
        {
            var bloques = Grilla();
            var rangos = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // Disponible sólo bloques 0..3 del lunes (7-11)
            var disp = new System.Collections.Generic.HashSet<int> { 0, 1, 2, 3 };

            var starts = BloquesPlanner.StartsValidos(2, bloques.Count, rangos, diaPorIdx, disp).ToArray();

            // Spans de 2 desde 0,1,2 son válidos (necesitan {0,1}, {1,2}, {2,3} — todos en disp)
            // Desde 3 NO es válido porque necesitaría {3,4} y 4 no está disponible.
            Assert.Equal(new[] { 0, 1, 2 }, starts);
        }

        [Fact]
        public void Solapan_SpansDistintosDias_Falso()
        {
            var bloques = Grilla();
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // Bloque 14 = lunes, bloque 15 = martes
            Assert.False(BloquesPlanner.Solapan(14, 1, 15, 1, diaPorIdx));
        }

        [Fact]
        public void Solapan_SpansMismoDiaContiguos_NoSolapan()
        {
            var bloques = Grilla();
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // [0,1) y [1,2) son contiguos pero NO solapan
            Assert.False(BloquesPlanner.Solapan(0, 1, 1, 1, diaPorIdx));
        }

        [Fact]
        public void Solapan_Span2hEnBloque0YSpan1hEnBloque1_SiSolapan()
        {
            var bloques = Grilla();
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // [0,2) y [1,2) sí solapan en el índice 1
            Assert.True(BloquesPlanner.Solapan(0, 2, 1, 1, diaPorIdx));
        }
    }
}
