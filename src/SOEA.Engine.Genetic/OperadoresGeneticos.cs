using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Operadores genéticos duration-aware: selección por torneo, cruce de un punto,
    /// mutación y reparación que detectan solapamiento usando spans
    /// [start, start + DuracionHoras).
    /// </summary>
    public class OperadoresGeneticos
    {
        private readonly Random _rng;
        private readonly List<Sesion> _sesiones;
        private readonly int _maxBloques;
        private readonly int _maxEspacios;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly Dictionary<DiaDeSemana, (int firstIdx, int lastIdx)> _rangosPorDia;
        private readonly int[] _duraciones;
        private readonly int[][] _startsValidosPorSesion;

        public OperadoresGeneticos(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            int maxEspacios,
            Random? rng = null)
        {
            _sesiones    = sesiones;
            _maxBloques  = bloques.Count;
            _maxEspacios = maxEspacios;
            _rng         = rng ?? new Random();

            _diaPorIdx     = BloquesPlanner.DiaPorBloqueIdx(bloques);
            _rangosPorDia  = BloquesPlanner.RangosPorDia(bloques);

            _duraciones = new int[sesiones.Count];
            _startsValidosPorSesion = new int[sesiones.Count][];
            for (int i = 0; i < sesiones.Count; i++)
            {
                _duraciones[i] = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));
                _startsValidosPorSesion[i] = BloquesPlanner
                    .StartsValidos(_duraciones[i], _maxBloques, _rangosPorDia, _diaPorIdx, bloquesDisponibles: null)
                    .ToArray();
            }
        }

        /// <summary>
        /// Compatibilidad con el constructor anterior (sin acceso a la grilla canónica de bloques).
        /// Crea un escenario degradado donde cada sesión se considera de 1h y todos los starts
        /// son válidos — usado únicamente por tests legacy que no construyen la grilla.
        /// </summary>
        public OperadoresGeneticos(
            List<Sesion> sesiones,
            int maxBloques,
            int maxEspacios,
            Random? rng = null)
        {
            _sesiones    = sesiones;
            _maxBloques  = maxBloques;
            _maxEspacios = maxEspacios;
            _rng         = rng ?? new Random();

            _diaPorIdx    = Enumerable.Repeat(DiaDeSemana.Lunes, maxBloques).ToArray();
            _rangosPorDia = new Dictionary<DiaDeSemana, (int, int)> { [DiaDeSemana.Lunes] = (0, Math.Max(0, maxBloques - 1)) };

            _duraciones = new int[sesiones.Count];
            _startsValidosPorSesion = new int[sesiones.Count][];
            for (int i = 0; i < sesiones.Count; i++)
            {
                _duraciones[i] = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));
                _startsValidosPorSesion[i] = Enumerable.Range(0, Math.Max(0, maxBloques - _duraciones[i] + 1)).ToArray();
            }
        }

        // ── Selección ────────────────────────────────────────────────────────────────

        public CromosomaHorario SeleccionTorneo(List<(CromosomaHorario cromosoma, decimal fitness)> poblacion, int k = 5)
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

        // ── Cruce y mutación ─────────────────────────────────────────────────────────

        public CromosomaHorario Cruce(CromosomaHorario padre1, CromosomaHorario padre2, double probabilidad = 0.8)
        {
            if (_rng.NextDouble() > probabilidad)
                return padre1.Clonar();

            int punto = _rng.Next(1, padre1.CantidadGenes);
            var bloques  = new int[padre1.CantidadGenes];
            var espacios = new int[padre1.CantidadGenes];

            for (int i = 0; i < padre1.CantidadGenes; i++)
            {
                if (i < punto)
                {
                    bloques[i]  = padre1.BloqueIndices[i];
                    espacios[i] = padre1.EspacioIndices[i];
                }
                else
                {
                    bloques[i]  = padre2.BloqueIndices[i];
                    espacios[i] = padre2.EspacioIndices[i];
                }
            }

            return new CromosomaHorario((Guid[])padre1.SesionIds.Clone(), bloques, espacios);
        }

        /// <summary>
        /// Mutación duration-aware: cada gen muta con probabilidad
        /// <paramref name="probabilidadPorGen"/>. El nuevo start se elige de la lista
        /// pre-calculada de inicios que no cruzan día para la duración de esa sesión.
        /// </summary>
        public void Mutar(CromosomaHorario cromosoma, double probabilidadPorGen = 0.05)
        {
            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                if (_rng.NextDouble() < probabilidadPorGen)
                {
                    var validos = _startsValidosPorSesion[i];
                    if (validos.Length > 0)
                        cromosoma.BloqueIndices[i] = validos[_rng.Next(validos.Length)];

                    if (_maxEspacios > 0)
                        cromosoma.EspacioIndices[i] = _rng.Next(_maxEspacios);
                }
            }
        }

        /// <summary>
        /// Crea una copia del cromosoma perturbando N genes respetando "no cruzar día".
        /// </summary>
        public CromosomaHorario ClonarYPerturbar(CromosomaHorario semilla, int perturbaciones = 3)
        {
            var clon = semilla.Clonar();
            for (int i = 0; i < perturbaciones && clon.CantidadGenes > 0; i++)
            {
                var idx = _rng.Next(clon.CantidadGenes);
                var validos = _startsValidosPorSesion[idx];
                if (validos.Length > 0)
                    clon.BloqueIndices[idx] = validos[_rng.Next(validos.Length)];

                if (_maxEspacios > 0)
                    clon.EspacioIndices[idx] = _rng.Next(_maxEspacios);
            }
            return clon;
        }

        // ── Reparación ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Operador de reparación: detecta solapamiento de spans
        /// [start, start+DuracionHoras) por (docente, día) y (espacio, día)
        /// y reasigna el gen ofensor a un start aleatorio que NO solape con los demás.
        /// </summary>
        public void Reparar(CromosomaHorario cromosoma)
        {
            RepararPorRecurso(
                cromosoma,
                clave: i => _sesiones[i].DocenteId == Guid.Empty
                    ? (object)("nop-" + i)
                    : _sesiones[i].DocenteId,
                aplicaA: _ => true,
                cambiarEspacio: false);

            if (_maxEspacios > 0)
            {
                RepararPorRecurso(
                    cromosoma,
                    clave: i => (object)cromosoma.EspacioIndices[i],
                    aplicaA: i => _sesiones[i].Modalidad != Modalidad.Virtual,
                    cambiarEspacio: true);
            }
        }

        private void RepararPorRecurso(
            CromosomaHorario cromosoma,
            Func<int, object> clave,
            Func<int, bool> aplicaA,
            bool cambiarEspacio)
        {
            // Para cada recurso, mantener la lista de genes ya colocados (idx, start, dur).
            var colocados = new Dictionary<object, List<(int gen, int start, int dur)>>();

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
            {
                if (!aplicaA(i)) continue;

                int start = cromosoma.BloqueIndices[i];
                int dur   = _duraciones[i];
                var k     = clave(i);

                if (!colocados.TryGetValue(k, out var lista))
                {
                    lista = new List<(int, int, int)>();
                    colocados[k] = lista;
                }

                bool solapa = lista.Any(p => BloquesPlanner.Solapan(p.start, p.dur, start, dur, _diaPorIdx));

                if (solapa)
                {
                    if (cambiarEspacio && _maxEspacios > 1)
                    {
                        // Estrategia 1: cambiar a un espacio libre en el mismo bloque/día.
                        var nuevoEspacio = BuscarEspacioLibre(cromosoma, i, start, dur);
                        if (nuevoEspacio.HasValue)
                        {
                            cromosoma.EspacioIndices[i] = nuevoEspacio.Value;
                            var nuevaClave = (object)nuevoEspacio.Value;
                            if (!colocados.TryGetValue(nuevaClave, out var lista2))
                            {
                                lista2 = new List<(int, int, int)>();
                                colocados[nuevaClave] = lista2;
                            }
                            lista2.Add((i, start, dur));
                            continue;
                        }
                    }

                    // Estrategia 2: mover a otro start válido que no solape con los del mismo recurso.
                    var nuevoStart = BuscarStartLibre(i, lista);
                    if (nuevoStart.HasValue)
                    {
                        cromosoma.BloqueIndices[i] = nuevoStart.Value;
                        lista.Add((i, nuevoStart.Value, dur));
                        continue;
                    }

                    // Fallback: registrar tal cual; el fitness penalizará.
                    lista.Add((i, start, dur));
                }
                else
                {
                    lista.Add((i, start, dur));
                }
            }
        }

        private int? BuscarStartLibre(int gen, List<(int gen, int start, int dur)> ocupados)
        {
            var validos = _startsValidosPorSesion[gen];
            if (validos.Length == 0) return null;
            int dur = _duraciones[gen];

            // Probar hasta 10 candidatos aleatorios; luego barrido lineal.
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

        private int? BuscarEspacioLibre(CromosomaHorario cromosoma, int gen, int start, int dur)
        {
            for (int intento = 0; intento < 10; intento++)
            {
                var cand = _rng.Next(_maxEspacios);
                if (cand == cromosoma.EspacioIndices[gen]) continue;

                bool conflicto = false;
                for (int j = 0; j < cromosoma.CantidadGenes; j++)
                {
                    if (j == gen) continue;
                    if (_sesiones[j].Modalidad == Modalidad.Virtual) continue;
                    if (cromosoma.EspacioIndices[j] != cand) continue;
                    if (BloquesPlanner.Solapan(cromosoma.BloqueIndices[j], _duraciones[j], start, dur, _diaPorIdx))
                    {
                        conflicto = true;
                        break;
                    }
                }

                if (!conflicto) return cand;
            }
            return null;
        }
    }
}
