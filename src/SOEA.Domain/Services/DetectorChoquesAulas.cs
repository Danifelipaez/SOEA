using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Franja donde la disponibilidad deja a más sesiones obligadas a coincidir de las que caben
    /// en las aulas que pueden usar.
    /// </summary>
    /// <param name="BloqueInicio">Índice del primer bloque de la franja.</param>
    /// <param name="BloqueFin">Índice del bloque siguiente al último (exclusivo).</param>
    /// <param name="SesionIds">Sesiones que deben dictarse a la vez en esa franja.</param>
    /// <param name="GrupoIds">Grupos de esas sesiones, sin repetir.</param>
    /// <param name="Aulas">Aulas distintas que esas sesiones pueden usar (menos que sesiones).</param>
    public sealed record ChoqueAulas(
        int BloqueInicio, int BloqueFin, IReadOnlyList<Guid> SesionIds, IReadOnlyList<Guid> GrupoIds, int Aulas);

    /// <summary>
    /// Condición necesaria de HC-S01 que se puede probar sin resolver: la "parte obligatoria" de una
    /// sesión son los bloques que ocupa empiece donde empiece dentro de su dominio — de su último
    /// inicio posible al final de su primer inicio posible. Si en un bloque y una semana hay más
    /// sesiones obligadas a estar ahí que aulas que les sirvan (condición de Hall sobre los
    /// conjuntos de aulas candidatas), ningún reparto de aulas existe.
    ///
    /// Es el caso típico del Excel importado: cada grupo trae una franja exacta de la duración de
    /// su clase, así que su dominio es un solo inicio. Sin este chequeo, CP-SAT solo decía "no
    /// alcanzan las aulas" sin nombrar la hora ni las clases que chocan.
    ///
    /// Los dominios y candidatas deben ser los MISMOS que usa CP-SAT
    /// (<see cref="CalculadorDominioSesion.StartsPermitidos"/>, <see cref="CalculadorEspaciosSesion.Candidatos"/>
    /// tras el aforo), así que un choque detectado aquí siempre es real. Que no detecte nada no
    /// prueba que el horario quepa.
    /// </summary>
    public static class DetectorChoquesAulas
    {
        /// <param name="startsPorSesion">Inicios válidos de cada sesión (índices de bloque).</param>
        /// <param name="candidatasPorSesion">Aulas candidatas de cada sesión que ocupa aula, ya filtradas por aforo.
        /// Una sesión sin candidatas no entra: ese fallo tiene su propio mensaje.</param>
        public static IReadOnlyList<ChoqueAulas> Detectar(
            IEnumerable<Sesion> sesiones,
            IReadOnlyDictionary<Guid, int[]> startsPorSesion,
            IReadOnlyDictionary<Guid, IReadOnlyList<int>> candidatasPorSesion)
        {
            var choques = new List<ChoqueAulas>();
            var lista = sesiones.ToList();

            foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
            {
                // Sesiones de la semana con parte obligatoria no vacía, por bloque cubierto.
                var obligadasPorBloque = new SortedDictionary<int, List<Sesion>>();
                foreach (var s in lista)
                {
                    if (!ModalidadSemanal.SemanasQueOcupanEspacio(s).Contains(semana)) continue;
                    if (!candidatasPorSesion.TryGetValue(s.Id, out var candidatas) || candidatas.Count == 0) continue;
                    if (!startsPorSesion.TryGetValue(s.Id, out var starts) || starts.Length == 0) continue;

                    int desde = starts.Max();
                    int hasta = starts.Min() + s.DuracionBloques;
                    for (int b = desde; b < hasta; b++)
                    {
                        if (!obligadasPorBloque.TryGetValue(b, out var enB))
                            obligadasPorBloque[b] = enB = new List<Sesion>();
                        enB.Add(s);
                    }
                }

                var deSemana = new List<ChoqueAulas>();
                foreach (var (bloque, enBloque) in obligadasPorBloque)
                {
                    var choque = ExcesoEnBloque(enBloque, candidatasPorSesion);
                    if (choque is null) continue;
                    var ids = choque.Value.Sesiones.Select(s => s.Id).OrderBy(id => id).ToList();
                    var grupos = choque.Value.Sesiones.Select(s => s.GrupoId).Distinct().OrderBy(id => id).ToList();

                    // Bloques consecutivos con las mismas sesiones son una sola franja. Como la parte
                    // obligatoria nunca cruza de día, el mismo conjunto implica el mismo día.
                    var previo = deSemana.Count > 0 ? deSemana[^1] : null;
                    if (previo is not null && previo.BloqueFin == bloque && previo.SesionIds.SequenceEqual(ids))
                        deSemana[^1] = previo with { BloqueFin = bloque + 1 };
                    else
                        deSemana.Add(new ChoqueAulas(bloque, bloque + 1, ids, grupos, choque.Value.Aulas));
                }

                // Una sesión que no alterna ocupa las dos semanas: el mismo choque no se repite.
                choques.AddRange(deSemana.Where(c => !choques.Any(x =>
                    x.BloqueInicio == c.BloqueInicio && x.BloqueFin == c.BloqueFin && x.SesionIds.SequenceEqual(c.SesionIds))));
            }

            return choques;
        }

        /// <summary>
        /// Condición de Hall restringida a los conjuntos de candidatas que aparecen (y su unión):
        /// para cada uno, las sesiones cuyas candidatas caben dentro no pueden ser más que sus aulas.
        /// Devuelve el subconjunto con mayor exceso, o null si no hay ninguno.
        /// </summary>
        private static (List<Sesion> Sesiones, int Aulas)? ExcesoEnBloque(
            List<Sesion> enBloque, IReadOnlyDictionary<Guid, IReadOnlyList<int>> candidatasPorSesion)
        {
            if (enBloque.Count < 2) return null;

            var conjuntos = enBloque.Select(s => candidatasPorSesion[s.Id].ToHashSet()).ToList();
            var union = conjuntos.SelectMany(c => c).ToHashSet();
            conjuntos.Add(union);

            (List<Sesion> Sesiones, int Aulas)? peor = null;
            foreach (var conjunto in conjuntos)
            {
                var dentro = enBloque.Where(s => candidatasPorSesion[s.Id].All(conjunto.Contains)).ToList();
                int exceso = dentro.Count - conjunto.Count;
                if (exceso > 0 && (peor is null || exceso > peor.Value.Sesiones.Count - peor.Value.Aulas))
                    peor = (dentro, conjunto.Count);
            }
            return peor;
        }
    }
}
