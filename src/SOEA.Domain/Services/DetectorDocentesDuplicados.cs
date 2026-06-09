using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.ValueObjects;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Detecta docentes que probablemente son la MISMA persona pero quedaron como registros
    /// distintos por variantes de nombre en el Excel (truncamientos, orden, grafía). Esto causa
    /// el síntoma "un docente con 2 sesiones a la misma hora": al fragmentarse en dos Guids, el
    /// motor los trata como personas diferentes y los agenda en paralelo sin violar ninguna
    /// restricción dura. La deduplicación por nombre normalizado (tildes/mayúsculas) NO cubre
    /// estos casos, así que aquí se DETECTAN para reporte — nunca se fusionan automáticamente,
    /// porque unir dos personas realmente distintas sería peor que el duplicado.
    ///
    /// Heurística conservadora (requiere coincidencia fuerte para reducir falsos positivos):
    ///   - mismo primer token (nombre de pila), y
    ///   - algún token de apellido comparte prefijo de ≥ <see cref="LongitudPrefijoApellido"/>
    ///     caracteres, o el conjunto de tokens de uno es subconjunto del otro.
    /// </summary>
    public static class DetectorDocentesDuplicados
    {
        public const int LongitudPrefijoApellido = 3;

        public readonly record struct Docente(Guid Id, string Nombre);

        /// <summary>
        /// Agrupa los docentes que parecen duplicados entre sí. Cada grupo del resultado contiene
        /// 2+ docentes sospechosos de ser la misma persona. Los docentes sin pareja no se incluyen.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<Docente>> AgruparPosiblesDuplicados(
            IEnumerable<Docente> docentes)
        {
            var lista = docentes
                .Where(d => !string.IsNullOrWhiteSpace(d.Nombre))
                .Select(d => (d, tokens: Tokenizar(d.Nombre)))
                .Where(x => x.tokens.Length > 0)
                .ToList();

            var grupos = new List<List<Docente>>();
            var asignado = new bool[lista.Count];

            for (int i = 0; i < lista.Count; i++)
            {
                if (asignado[i]) continue;
                List<Docente>? grupo = null;

                for (int j = i + 1; j < lista.Count; j++)
                {
                    if (asignado[j]) continue;
                    if (lista[i].d.Id == lista[j].d.Id) continue;
                    if (!SonProbablesDuplicados(lista[i].tokens, lista[j].tokens)) continue;

                    grupo ??= new List<Docente> { lista[i].d };
                    grupo.Add(lista[j].d);
                    asignado[j] = true;
                }

                if (grupo != null)
                {
                    asignado[i] = true;
                    grupos.Add(grupo);
                }
            }

            return grupos;
        }

        private static bool SonProbablesDuplicados(string[] a, string[] b)
        {
            // Mismo nombre de pila (primer token).
            if (a[0] != b[0]) return false;

            var setA = new HashSet<string>(a);
            var setB = new HashSet<string>(b);

            // Un conjunto de tokens es subconjunto del otro (p. ej. faltan segundos nombres).
            if (setA.IsSubsetOf(setB) || setB.IsSubsetOf(setA)) return true;

            // Algún token "largo" de uno es prefijo de un token del otro (trunca­miento de apellido),
            // p. ej. "vil" vs "villamizar". Se exige prefijo de longitud suficiente para evitar
            // coincidencias triviales.
            foreach (var ta in a.Skip(1))
                foreach (var tb in b.Skip(1))
                {
                    if (ta == tb) continue;
                    var min = Math.Min(ta.Length, tb.Length);
                    if (min >= LongitudPrefijoApellido &&
                        (ta.StartsWith(tb, StringComparison.Ordinal) || tb.StartsWith(ta, StringComparison.Ordinal)))
                        return true;
                }

            return false;
        }

        private static string[] Tokenizar(string nombre) =>
            NormalizadorTexto.Normalizar(nombre)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
