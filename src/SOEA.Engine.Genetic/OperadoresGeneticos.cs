using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Operadores genéticos del modelo bi-semanal. Operan SOLO sobre el inicio de cada sesión
    /// (compartido A/B). Invariantes preservados por construcción:
    ///   I1: todo inicio elegido pertenece a <see cref="_startsValidos"/>[i] = cabe-en-día ∩
    ///       disponibilidad del docente (HC-I02). Mutación, cruce y perturbación solo eligen de ahí.
    ///   Regla 9: hay un único inicio por sesión ⇒ A y B nunca se desincronizan.
    /// La reparación elimina solapes de docente (HC-I01). Como los inicios son compartidos, el
    /// horario del docente es idéntico en A y B, así que basta reparar una vez. Los aulas (HC-S01)
    /// se resuelven en un pase posterior (<see cref="AsignadorEspacios"/>), no aquí.
    /// </summary>
    public class OperadoresGeneticos
    {
        private readonly Random _rng;
        private readonly List<Sesion> _sesiones;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly int[] _duraciones;
        private readonly int[][] _startsValidos;

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

            _duraciones    = new int[sesiones.Count];
            _startsValidos = new int[sesiones.Count][];
            for (int i = 0; i < sesiones.Count; i++)
            {
                _duraciones[i] = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));

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
        // Hereda el inicio de cada sesión como unidad desde uno de los padres ⇒ I1 y regla 9
        // se preservan (ambos padres ya los satisfacen para esa sesión).
        public CromosomaHorario Cruce(CromosomaHorario p1, CromosomaHorario p2, double probabilidad = 0.8)
        {
            if (p1.CantidadGenes < 2 || _rng.NextDouble() > probabilidad)
                return p1.Clonar();

            int punto = _rng.Next(1, p1.CantidadGenes);
            var start = new int[p1.CantidadGenes];
            for (int i = 0; i < p1.CantidadGenes; i++)
                start[i] = i < punto ? p1.Start[i] : p2.Start[i];

            return new CromosomaHorario((Guid[])p1.SesionIds.Clone(), start);
        }

        // ── Mutación: re-elige el inicio entre los válidos de esa sesión ──────────────
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
                    clon.Start[i] = validos[_rng.Next(validos.Length)];
            }
            return clon;
        }

        // ── Reparación HC-I01: sin solapes de docente (una pasada; A≡B por inicio compartido) ─
        public void Reparar(CromosomaHorario cromosoma)
        {
            var colocadosPorDocente = new Dictionary<Guid, List<(int start, int dur)>>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                var docente = _sesiones[i].DocenteId;
                if (docente == Guid.Empty) continue;

                int start = cromosoma.Start[i];
                int dur   = _duraciones[i];

                if (!colocadosPorDocente.TryGetValue(docente, out var lista))
                {
                    lista = new List<(int, int)>();
                    colocadosPorDocente[docente] = lista;
                }

                bool solapa = lista.Any(p => BloquesPlanner.Solapan(p.start, p.dur, start, dur, _diaPorIdx));
                if (solapa)
                {
                    var nuevo = BuscarStartLibre(i, lista);
                    if (nuevo.HasValue)
                    {
                        cromosoma.Start[i] = nuevo.Value;
                        lista.Add((nuevo.Value, dur));
                        continue;
                    }
                    // Fallback: registrar tal cual. El post-chequeo del orquestador (HC-I01) lo
                    // detecta y hace fallback a Fase 2 — nunca se publica un horario inválido.
                    lista.Add((start, dur));
                }
                else
                {
                    lista.Add((start, dur));
                }
            }
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
