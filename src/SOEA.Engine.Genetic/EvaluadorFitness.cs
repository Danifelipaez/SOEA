using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Fitness del modelo bi-semanal (menor = mejor). Los tres objetivos son TEMPORALES y
    /// centrados en el docente — ninguno depende del aula, por eso el aula no está en el cromosoma:
    ///   ① SC-01: minimizar huecos ociosos entre sesiones del mismo docente en un día.
    ///   ② SC-09: penalizar rachas de > 6 horas SEGUIDAS de sesiones (restricción BLANDA fuerte).
    ///   ③ SC-06: balancear la carga del docente entre los días que tiene DISPONIBLES (desviación
    ///            media absoluta sobre todos los días disponibles, contando ceros).
    /// Como los inicios son compartidos A/B, las dos semanas son idénticas en timing: estos tres
    /// objetivos se calculan UNA vez (no por semana).
    ///
    /// Además, una GUARDA de factibilidad de aulas (no es un objetivo blando): penaliza fuerte si en
    /// alguna (semana, día) hay más sesiones presenciales simultáneas que aulas. Mantiene al GA en la
    /// región donde el pase posterior de asignación de aulas (HC-S01) es factible.
    /// </summary>
    public class EvaluadorFitness
    {
        private const int PesoFactibilidadSalas = 1000;

        private readonly List<Sesion> _sesiones;
        private readonly List<BloqueTiempo> _bloques;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly int[] _duraciones;
        private readonly int _nEspacios;
        private readonly Dictionary<Guid, HashSet<DiaDeSemana>> _diasDisponiblesPorDocente;

        private readonly int _pesoSC01; // huecos
        private readonly int _pesoSC06; // balance entre días
        private readonly int _pesoSC09; // > 6 horas seguidas

        public EvaluadorFitness(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Docente> docentes,
            List<Espacio> espacios,
            ConfiguracionOptimizacion? config = null)
        {
            _sesiones  = sesiones;
            _bloques   = bloques;
            _nEspacios = espacios.Count;
            _diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            _duraciones = new int[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
                _duraciones[i] = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));

            _diasDisponiblesPorDocente = docentes.ToDictionary(
                d => d.Id,
                d => d.BloquesDisponibles.Select(b => b.Dia).ToHashSet());

            _pesoSC01 = config?.PesoErgo     ?? 3;
            _pesoSC06 = config?.PesoTiempos  ?? 2;
            _pesoSC09 = config?.PesoAlmuerzo ?? 3;
        }

        public decimal Evaluar(CromosomaHorario c)
        {
            var spansPorDocenteDia = SpansPorDocenteDia(c);
            decimal fitness = 0;
            fitness += _pesoSC01 * SC01_HuecosOciosos(spansPorDocenteDia);
            fitness += _pesoSC06 * SC06_BalanceEntreDias(c);
            fitness += _pesoSC09 * SC09_HorasSeguidas(spansPorDocenteDia);
            fitness += PesoFactibilidadSalas * GuardaCapacidadAulas(c);
            return fitness;
        }

        // ── Agrupa los genes de cada docente por día, como spans [start, start+dur) en bloques. ──
        private Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> SpansPorDocenteDia(CromosomaHorario c)
        {
            var mapa = new Dictionary<Guid, Dictionary<DiaDeSemana, List<(int, int)>>>();
            for (int i = 0; i < c.CantidadGenes; i++)
            {
                int start = c.Start[i];
                if (start < 0 || start >= _bloques.Count) continue;
                var docente = _sesiones[i].DocenteId;
                var dia = _diaPorIdx[start];

                if (!mapa.TryGetValue(docente, out var porDia)) { porDia = new(); mapa[docente] = porDia; }
                if (!porDia.TryGetValue(dia, out var lista)) { lista = new(); porDia[dia] = lista; }
                lista.Add((start, _duraciones[i]));
            }
            return mapa;
        }

        // ① SC-01: suma de huecos (en horas) entre sesiones consecutivas del mismo docente por día.
        private int SC01_HuecosOciosos(Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> spansPorDocenteDia)
        {
            int huecos = 0;
            foreach (var porDia in spansPorDocenteDia.Values)
                foreach (var spans in porDia.Values)
                {
                    var ord = spans.OrderBy(s => s.start).ToList();
                    for (int i = 1; i < ord.Count; i++)
                    {
                        int finAnterior = ord[i - 1].start + ord[i - 1].dur;
                        int gap = ord[i].start - finAnterior;
                        if (gap > 0) huecos += gap;
                    }
                }
            return huecos;
        }

        // ② SC-09: por cada racha contigua de sesiones, penaliza las horas que excedan de 6.
        private int SC09_HorasSeguidas(Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> spansPorDocenteDia)
        {
            int penalizacion = 0;
            foreach (var porDia in spansPorDocenteDia.Values)
                foreach (var spans in porDia.Values)
                {
                    var ord = spans.OrderBy(s => s.start).ToList();
                    int? rachaInicio = null, rachaFin = null;
                    foreach (var (start, dur) in ord)
                    {
                        if (rachaInicio is null)
                        {
                            rachaInicio = start; rachaFin = start + dur;
                        }
                        else if (start <= rachaFin) // contigua (o solapada): extiende la racha
                        {
                            rachaFin = Math.Max(rachaFin!.Value, start + dur);
                        }
                        else // hay hueco: cierra la racha y abre una nueva
                        {
                            penalizacion += Math.Max(0, (rachaFin!.Value - rachaInicio.Value) - 6);
                            rachaInicio = start; rachaFin = start + dur;
                        }
                    }
                    if (rachaInicio is not null)
                        penalizacion += Math.Max(0, (rachaFin!.Value - rachaInicio.Value) - 6);
                }
            return penalizacion;
        }

        // ③ SC-06: desviación media absoluta de la carga del docente sobre sus días DISPONIBLES.
        private int SC06_BalanceEntreDias(CromosomaHorario c)
        {
            // Carga real (horas) por docente y día.
            var cargaPorDocenteDia = new Dictionary<Guid, Dictionary<DiaDeSemana, decimal>>();
            for (int i = 0; i < c.CantidadGenes; i++)
            {
                int start = c.Start[i];
                if (start < 0 || start >= _bloques.Count) continue;
                var docente = _sesiones[i].DocenteId;
                var dia = _diaPorIdx[start];

                if (!cargaPorDocenteDia.TryGetValue(docente, out var porDia)) { porDia = new(); cargaPorDocenteDia[docente] = porDia; }
                porDia.TryGetValue(dia, out var actual);
                porDia[dia] = actual + _sesiones[i].DuracionHoras;
            }

            decimal penalizacion = 0;
            foreach (var (docente, cargaDia) in cargaPorDocenteDia)
            {
                // Buckets = días disponibles del docente (incluye ceros). Si no hay info, usar los días con carga.
                var diasBucket = _diasDisponiblesPorDocente.TryGetValue(docente, out var disp) && disp.Count > 0
                    ? disp
                    : cargaDia.Keys.ToHashSet();
                if (diasBucket.Count <= 1) continue;

                decimal total = cargaDia.Values.Sum();
                decimal media = total / diasBucket.Count;
                decimal mad = diasBucket.Sum(d => Math.Abs((cargaDia.TryGetValue(d, out var l) ? l : 0m) - media));
                penalizacion += mad;
            }
            return (int)Math.Ceiling(penalizacion);
        }

        // Guarda de aulas: máx. sesiones presenciales simultáneas por (semana, día) vs nº de aulas.
        private int GuardaCapacidadAulas(CromosomaHorario c)
        {
            int exceso = 0;
            foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
            {
                // Spans presenciales en esta semana, agrupados por día.
                var porDia = new Dictionary<DiaDeSemana, List<(int start, int dur)>>();
                for (int i = 0; i < c.CantidadGenes; i++)
                {
                    if (ModalidadSemanal.Derivar(_sesiones[i], semana) != Modalidad.Presencial) continue;
                    int start = c.Start[i];
                    if (start < 0 || start >= _bloques.Count) continue;
                    var dia = _diaPorIdx[start];
                    if (!porDia.TryGetValue(dia, out var lista)) { lista = new(); porDia[dia] = lista; }
                    lista.Add((start, _duraciones[i]));
                }

                foreach (var spans in porDia.Values)
                {
                    int maxConcurrentes = MaxConcurrencia(spans);
                    if (maxConcurrentes > _nEspacios)
                        exceso += maxConcurrentes - _nEspacios;
                }
            }
            return exceso;
        }

        // Barrido: máximo nº de intervalos solapados a la vez.
        private static int MaxConcurrencia(List<(int start, int dur)> spans)
        {
            var eventos = new List<(int t, int delta)>(spans.Count * 2);
            foreach (var (start, dur) in spans)
            {
                eventos.Add((start, +1));
                eventos.Add((start + dur, -1));
            }
            // Orden: por tiempo; a igual tiempo, las salidas (-1) antes que las entradas (+1).
            eventos.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.delta.CompareTo(b.delta));

            int actual = 0, max = 0;
            foreach (var (_, delta) in eventos)
            {
                actual += delta;
                if (actual > max) max = actual;
            }
            return max;
        }
    }
}
