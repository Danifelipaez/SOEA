using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Operadores genéticos del modelo bi-semanal (Incremento 2). Cada sesión tiene dos genes de
    /// inicio: <c>Start</c> (Semana A) y <c>StartB</c> (Semana B). Invariantes preservados por
    /// construcción:
    ///   I1: todo inicio elegido pertenece a <see cref="_startsValidos"/>[i] = cabe-en-día ∩
    ///       disponibilidad del docente (HC-I02). Mutación, cruce y perturbación solo eligen de
    ///       ahí, para AMBOS genes.
    ///   Regla 9 / ALT-05: para TipoA/TipoB, <c>StartB[i] == Start[i]</c> siempre — los operadores
    ///       solo mueven StartB de forma independiente para SinAlternancia (ALT-06);
    ///       <see cref="Reparar"/> re-sincroniza incondicionalmente el resto en cada llamada.
    /// La reparación elimina solapes de docente (HC-I01) en dos pasadas: Semana A (Start) y luego
    /// Semana B (StartB), tratando los genes TipoA/TipoB como fijos en la segunda pasada (su
    /// franja de Semana B ya quedó resuelta en la primera, por la regla 9). Los aulas (HC-S01) se
    /// resuelven en un pase posterior (<see cref="AsignadorEspacios"/>), no aquí.
    /// </summary>
    public class OperadoresGeneticos
    {
        private readonly Random _rng;
        private readonly List<Sesion> _sesiones;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly int[] _duraciones;
        private readonly int[][] _startsValidos;
        private readonly bool[] _esIndependiente; // true = SinAlternancia (ALT-06: StartB libre)

        public OperadoresGeneticos(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            IReadOnlyList<Docente> docentes,
            Random rng)
        {
            _sesiones  = sesiones;
            _rng       = rng;
            _diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var rangos = BloquesPlanner.RangosPorDia(bloques);

            var bloqueIndex = new Dictionary<Guid, int>(bloques.Count);
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;

            var docentePorId = docentes.ToDictionary(d => d.Id);

            _duraciones      = new int[sesiones.Count];
            _startsValidos   = new int[sesiones.Count][];
            _esIndependiente = new bool[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
            {
                _duraciones[i]      = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));
                _esIndependiente[i] = sesiones[i].Alternancia == TipoAlternancia.SinAlternancia;

                // HC-I02: filtrar los inicios por los bloques disponibles del docente.
                ISet<int>? disponibles = null;
                if (docentePorId.TryGetValue(sesiones[i].DocenteId, out var doc))
                {
                    disponibles = doc.BloquesDisponibles
                        .Where(b => bloqueIndex.ContainsKey(b.Id))
                        .Select(b => bloqueIndex[b.Id])
                        .ToHashSet();
                    // Sin info de disponibilidad ⇒ sin filtro (no debería ocurrir: Fase 2 ya
                    // rechaza a un docente sin bloques que calcen con la grilla).
                    if (disponibles.Count == 0) disponibles = null;
                }

                _startsValidos[i] = BloquesPlanner
                    .StartsValidos(_duraciones[i], bloques.Count, rangos, _diaPorIdx, disponibles)
                    .ToArray();
            }
        }

        public IReadOnlyList<int> StartsValidosDe(int sesionIdx) => _startsValidos[sesionIdx];
        public int DuracionDe(int sesionIdx) => _duraciones[sesionIdx];

        // ── Selección ────────────────────────────────────────────────────────────────
        public CromosomaHorario SeleccionTorneo(
            List<(CromosomaHorario cromosoma, decimal fitness)> poblacion, int k = 5)
        {
            CromosomaHorario? mejor = null;
            decimal mejorFitness = decimal.MaxValue;
            for (int i = 0; i < k; i++)
            {
                var idx = _rng.Next(poblacion.Count);
                if (poblacion[idx].fitness < mejorFitness)
                {
                    mejorFitness = poblacion[idx].fitness;
                    mejor = poblacion[idx].cromosoma;
                }
            }
            return mejor!;
        }

        // ── Cruce de un punto (a granularidad de sesión) ──────────────────────────────
        // Hereda AMBOS genes (Start y StartB) de cada sesión como unidad desde uno de los padres,
        // con el MISMO punto de corte para los dos arreglos ⇒ I1 y regla 9 se preservan (ambos
        // padres ya los satisfacen para esa sesión).
        public CromosomaHorario Cruce(CromosomaHorario p1, CromosomaHorario p2, double probabilidad = 0.8)
        {
            if (p1.CantidadGenes < 2 || _rng.NextDouble() > probabilidad)
                return p1.Clonar();

            int punto = _rng.Next(1, p1.CantidadGenes);
            var start  = new int[p1.CantidadGenes];
            var startB = new int[p1.CantidadGenes];
            for (int i = 0; i < p1.CantidadGenes; i++)
            {
                start[i]  = i < punto ? p1.Start[i]  : p2.Start[i];
                startB[i] = i < punto ? p1.StartB[i] : p2.StartB[i];
            }

            return new CromosomaHorario((Guid[])p1.SesionIds.Clone(), start, startB);
        }

        // ── Mutación: re-elige el inicio entre los válidos de esa sesión ──────────────
        // Start (Semana A) muta para toda sesión, igual que en Incremento 1. StartB (Semana B)
        // muta de forma independiente SOLO para SinAlternancia (ALT-06); en TipoA/TipoB,
        // Reparar la resincroniza después, así que tocarla aquí sería trabajo descartado.
        public void Mutar(CromosomaHorario cromosoma, double probabilidadPorGen = 0.05)
        {
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                if (_rng.NextDouble() < probabilidadPorGen)
                {
                    var validos = _startsValidos[i];
                    if (validos.Length > 0)
                        cromosoma.Start[i] = validos[_rng.Next(validos.Length)];
                }

                if (_esIndependiente[i] && _rng.NextDouble() < probabilidadPorGen)
                {
                    var validos = _startsValidos[i];
                    if (validos.Length > 0)
                        cromosoma.StartB[i] = validos[_rng.Next(validos.Length)];
                }
            }
        }

        public CromosomaHorario ClonarYPerturbar(CromosomaHorario semilla, int perturbaciones = 3)
        {
            var clon = semilla.Clonar();
            for (int k = 0; k < perturbaciones && clon.CantidadGenes > 0; k++)
            {
                var i = _rng.Next(clon.CantidadGenes);
                var validos = _startsValidos[i];
                if (validos.Length > 0)
                {
                    clon.Start[i] = validos[_rng.Next(validos.Length)];
                    if (_esIndependiente[i])
                        clon.StartB[i] = validos[_rng.Next(validos.Length)];
                }
            }
            return clon;
        }

        // ── Reparación HC-I01: sin solapes de docente, en dos pasadas ─────────────────
        // Pasada A: idéntica al comportamiento de Incremento 1, sobre Start[]. El resultado se
        // refleja incondicionalmente a StartB para TipoA/TipoB (regla 9: misma franja en ambas
        // semanas). Pasada B: solo repara StartB de SinAlternancia; TipoA/TipoB entran ya
        // colocados como obstáculos fijos (su franja de Semana B es idéntica a la de Semana A,
        // ya resuelta en la pasada A, así que dos genes ligados nunca pueden chocar en B sin
        // haber chocado ya en A).
        public void Reparar(CromosomaHorario cromosoma)
        {
            RepararSemana(cromosoma.Start, movible: _ => true);

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
                if (!_esIndependiente[i])
                    cromosoma.StartB[i] = cromosoma.Start[i];

            RepararSemana(cromosoma.StartB, movible: i => _esIndependiente[i]);
        }

        private void RepararSemana(int[] starts, Func<int, bool> movible)
        {
            var colocadosPorDocente = new Dictionary<Guid, List<(int start, int dur)>>();

            // 1) Genes fijos primero: solo registran su ocupación (no se mueven). Necesario para
            //    que los genes movibles, procesados después, vean el panorama completo y no
            //    elijan un inicio que ya choca con un gen fijo que aún no se había registrado.
            for (int i = 0; i < starts.Length; i++)
            {
                if (movible(i)) continue;
                var docente = _sesiones[i].DocenteId;
                if (docente == Guid.Empty) continue;
                ObtenerLista(colocadosPorDocente, docente).Add((starts[i], _duraciones[i]));
            }

            // 2) Genes movibles, en orden: se reparan contra todo lo ya colocado (fijos + movibles
            //    previos de esta misma pasada).
            for (int i = 0; i < starts.Length; i++)
            {
                if (!movible(i)) continue;
                var docente = _sesiones[i].DocenteId;
                if (docente == Guid.Empty) continue;

                int start = starts[i];
                int dur   = _duraciones[i];
                var lista = ObtenerLista(colocadosPorDocente, docente);

                bool solapa = lista.Any(p => BloquesPlanner.Solapan(p.start, p.dur, start, dur, _diaPorIdx));
                if (solapa)
                {
                    var nuevo = BuscarStartLibre(i, lista);
                    if (nuevo.HasValue) start = nuevo.Value;
                    // Si no hay libre, fallback: queda tal cual. El post-chequeo del orquestador
                    // (HC-I01) lo detecta y hace fallback a Fase 2 — nunca se publica un horario inválido.
                }

                starts[i] = start;
                lista.Add((start, dur));
            }
        }

        private static List<(int start, int dur)> ObtenerLista(
            Dictionary<Guid, List<(int start, int dur)>> mapa, Guid docente)
        {
            if (!mapa.TryGetValue(docente, out var lista))
            {
                lista = new List<(int, int)>();
                mapa[docente] = lista;
            }
            return lista;
        }

        private int? BuscarStartLibre(int gen, List<(int start, int dur)> ocupados)
        {
            var validos = _startsValidos[gen];
            if (validos.Length == 0) return null;
            int dur = _duraciones[gen];

            for (int intento = 0; intento < 10; intento++)
            {
                var cand = validos[_rng.Next(validos.Length)];
                if (!ocupados.Any(o => BloquesPlanner.Solapan(o.start, o.dur, cand, dur, _diaPorIdx)))
                    return cand;
            }
            foreach (var cand in validos)
            {
                if (!ocupados.Any(o => BloquesPlanner.Solapan(o.start, o.dur, cand, dur, _diaPorIdx)))
                    return cand;
            }
            return null;
        }
    }
}
