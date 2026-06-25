using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Operaciones puras sobre la grilla canónica de bloques de tiempo.
    /// Centraliza el cálculo de rangos por día, validación de spans multi-bloque
    /// y starts válidos para una sesión con duración dada.
    /// Stateless: usable por Application y por los Engines sin acoplamiento.
    /// </summary>
    public static class BloquesPlanner
    {
        /// <summary>
        /// Para cada día presente en <paramref name="bloques"/>, devuelve el rango
        /// [firstIdx, lastIdx] de índices globales que pertenecen a ese día.
        /// El orden del array <paramref name="bloques"/> es la verdad — los índices
        /// son posiciones en esa lista, no IDs.
        /// </summary>
        public static Dictionary<DiaDeSemana, (int firstIdx, int lastIdx)> RangosPorDia(
            IReadOnlyList<BloqueTiempo> bloques)
        {
            var rangos = new Dictionary<DiaDeSemana, (int, int)>();
            for (int i = 0; i < bloques.Count; i++)
            {
                var dia = bloques[i].Dia;
                if (rangos.TryGetValue(dia, out var r))
                    rangos[dia] = (r.Item1, i);
                else
                    rangos[dia] = (i, i);
            }
            return rangos;
        }

        /// <summary>
        /// Mapeo paralelo a <paramref name="bloques"/>: <c>diaPorIdx[i] = bloques[i].Dia</c>.
        /// Útil para comparaciones rápidas de "misma jornada" sin volver a buscar.
        /// </summary>
        public static DiaDeSemana[] DiaPorBloqueIdx(IReadOnlyList<BloqueTiempo> bloques)
            => bloques.Select(b => b.Dia).ToArray();

        /// <summary>
        /// Verifica que un span [startIdx, startIdx+duracion) cabe en el mismo día,
        /// es decir, no cruza al día siguiente de la grilla canónica.
        /// </summary>
        public static bool CabeEnDia(
            int startIdx,
            int duracion,
            IDictionary<DiaDeSemana, (int firstIdx, int lastIdx)> rangos,
            DiaDeSemana[] diaPorIdx)
        {
            if (startIdx < 0 || startIdx >= diaPorIdx.Length) return false;
            if (duracion <= 0) return false;
            var diaInicio = diaPorIdx[startIdx];
            var lastIdx = rangos[diaInicio].lastIdx;
            return startIdx + duracion - 1 <= lastIdx;
        }

        /// <summary>
        /// Devuelve los índices de inicio válidos para una sesión de
        /// <paramref name="duracion"/> horas, considerando:
        /// (a) que todos los bloques cubiertos están en el mismo día,
        /// (b) que todos los bloques cubiertos están en <paramref name="bloquesDisponibles"/>.
        /// Si <paramref name="bloquesDisponibles"/> es null se omite el filtro (b).
        /// </summary>
        public static IEnumerable<int> StartsValidos(
            int duracion,
            int totalBloques,
            IDictionary<DiaDeSemana, (int firstIdx, int lastIdx)> rangos,
            DiaDeSemana[] diaPorIdx,
            ISet<int>? bloquesDisponibles = null)
        {
            for (int start = 0; start <= totalBloques - duracion; start++)
            {
                if (!CabeEnDia(start, duracion, rangos, diaPorIdx)) continue;

                if (bloquesDisponibles != null)
                {
                    bool todos = true;
                    for (int k = 0; k < duracion; k++)
                    {
                        if (!bloquesDisponibles.Contains(start + k)) { todos = false; break; }
                    }
                    if (!todos) continue;
                }

                yield return start;
            }
        }

        /// <summary>
        /// Dos spans [a, a+durA) y [b, b+durB) solapan si y solo si están en el
        /// mismo día y comparten al menos un índice de bloque.
        /// </summary>
        public static bool Solapan(
            int startA, int durA,
            int startB, int durB,
            DiaDeSemana[] diaPorIdx)
        {
            if (startA < 0 || startB < 0) return false;
            if (startA >= diaPorIdx.Length || startB >= diaPorIdx.Length) return false;
            if (diaPorIdx[startA] != diaPorIdx[startB]) return false;
            return startA < startB + durB && startB < startA + durA;
        }
    }
}
