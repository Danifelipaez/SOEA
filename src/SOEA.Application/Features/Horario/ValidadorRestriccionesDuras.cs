using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Validador post-generación de restricciones duras (P0.3 auditoría).
    /// Recorre las asignaciones semanales FINALES (salida del motor) y cuenta violaciones reales
    /// antes de construir/publicar el <see cref="Domain.Entities.Horario"/>, en lugar de confiar en
    /// un <c>violacionesRestriccionesDuras: 0</c> hardcodeado. Detecta:
    ///   - HC-I01: un mismo docente con sesiones solapadas en la misma semana (presencial o virtual).
    ///   - HC-S01: un mismo espacio físico con sesiones presenciales solapadas en la misma semana.
    /// La detección es consciente de la duración: cada asignación ocupa <c>ceil(DuracionHoras)</c>
    /// bloques contiguos a partir de su bloque de inicio. Como la grilla canónica se indexa por
    /// (día, hora), dos intervalos en días distintos tienen rangos de índice disjuntos.
    /// </summary>
    public static class ValidadorRestriccionesDuras
    {
        public static IReadOnlyList<string> Validar(
            IEnumerable<AsignacionSemanal> asignaciones,
            IReadOnlyDictionary<Guid, Sesion> sesionPorId,
            IReadOnlyDictionary<Guid, int> bloqueIndex)
        {
            var conflictos = new List<string>();

            var items = new List<Intervalo>();
            foreach (var a in asignaciones)
            {
                if (!sesionPorId.TryGetValue(a.SesionId, out var s)) continue;
                if (!bloqueIndex.TryGetValue(a.BloqueTiempoId, out var inicio)) continue;
                int duracion = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                items.Add(new Intervalo(a, s, inicio, duracion));
            }

            // HC-I01 — conflicto de docente (presencial + virtual consumen tiempo del docente).
            foreach (var grupo in items.GroupBy(i => (i.Sesion.DocenteId, i.Asignacion.Semana)))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-I01", $"docente {grupo.Key.DocenteId} (semana {grupo.Key.Semana})"));

            // HC-S01 — conflicto de espacio físico (solo presencial; las virtuales no ocupan espacio).
            foreach (var grupo in items
                         .Where(i => i.Asignacion.Modalidad == Modalidad.Presencial && i.Asignacion.EspacioId.HasValue)
                         .GroupBy(i => (Espacio: i.Asignacion.EspacioId!.Value, i.Asignacion.Semana)))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-S01", $"espacio {grupo.Key.Espacio} (semana {grupo.Key.Semana})"));

            return conflictos;
        }

        /// <summary>
        /// HC-I03: ningún docente debe exceder su máximo de horas semanales. La carga semanal es la
        /// suma de duraciones de SUS sesiones (cada sesión cuenta una vez por semana; la modalidad
        /// virtual es sincrónica y también consume tiempo). Se valida sobre las sesiones lógicas, no
        /// sobre las asignaciones (que son dos por sesión), para no duplicar el conteo.
        /// </summary>
        public static IReadOnlyList<string> ValidarCargaSemanal(
            IEnumerable<Sesion> sesiones,
            IReadOnlyDictionary<Guid, decimal> maxHorasPorDocente)
        {
            var conflictos = new List<string>();
            foreach (var grupo in sesiones.Where(s => s.DocenteId != Guid.Empty).GroupBy(s => s.DocenteId))
            {
                if (!maxHorasPorDocente.TryGetValue(grupo.Key, out var max)) continue;
                var total = grupo.Sum(s => s.DuracionHoras);
                if (total > max)
                    conflictos.Add($"HC-I03: docente {grupo.Key} tiene {total}h asignadas por semana, excede su máximo de {max}h.");
            }
            return conflictos;
        }

        private static IEnumerable<string> DetectarSolapes(
            IEnumerable<Intervalo> grupo, string regla, string contexto)
        {
            var ordenados = grupo.OrderBy(i => i.Inicio).ToList();
            for (int i = 0; i < ordenados.Count; i++)
            {
                for (int j = i + 1; j < ordenados.Count; j++)
                {
                    // Como están ordenados por inicio, basta comparar contra el fin del primero.
                    if (ordenados[j].Inicio >= ordenados[i].Fin) break;
                    yield return $"{regla}: solape en {contexto} entre sesiones {ordenados[i].Sesion.Id} y {ordenados[j].Sesion.Id}.";
                }
            }
        }

        private readonly struct Intervalo
        {
            public Intervalo(AsignacionSemanal asignacion, Sesion sesion, int inicio, int duracion)
            {
                Asignacion = asignacion;
                Sesion = sesion;
                Inicio = inicio;
                Duracion = duracion;
            }

            public AsignacionSemanal Asignacion { get; }
            public Sesion Sesion { get; }
            public int Inicio { get; }
            public int Duracion { get; }
            public int Fin => Inicio + Duracion;
        }
    }
}
