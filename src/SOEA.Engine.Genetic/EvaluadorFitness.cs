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
    /// Fitness del modelo bi-semanal (menor = mejor). Los tres objetivos temporales y centrados en
    /// el docente — ninguno depende del aula, por eso el aula no está en el cromosoma:
    ///   ① SC-01: minimizar huecos ociosos entre sesiones del mismo docente en un día.
    ///   ② SC-09: penalizar rachas de > 6 horas SEGUIDAS de sesiones (restricción BLANDA fuerte).
    ///   ③ SC-06: balancear la carga del docente entre los días que tiene DISPONIBLES (desviación
    ///            media absoluta sobre todos los días disponibles, contando ceros).
    /// Desde el Incremento 2, cada sesión tiene un inicio por semana (<c>Start</c>/<c>StartB</c>),
    /// así que ①②③ se calculan UNA VEZ POR SEMANA (con el arreglo de inicios correspondiente) y se
    /// suman. Para TipoA/TipoB las dos semanas son idénticas en timing (StartB==Start por
    /// construcción), así que su aporte se duplica sin distorsionar el balance entre sesiones; solo
    /// SinAlternancia puede hacer que el aporte de A y B difiera.
    ///   ④ SC-BAL (nuevo, Incremento 2): por docente y día, penaliza la diferencia de carga horaria
    ///      total entre Semana A y Semana B. Los genes TipoA/TipoB nunca contribuyen a esta
    ///      diferencia (StartB==Start ⇒ mismo día, mismas horas en ambas semanas); solo
    ///      SinAlternancia puede introducir o resolver desbalance, aprovechando la libertad de ALT-06.
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

        private readonly int _pesoSC01;  // huecos
        private readonly int _pesoSC06;  // balance entre días
        private readonly int _pesoSC09;  // > 6 horas seguidas
        private readonly int _pesoSCBAL; // balance entre semanas A/B (Incremento 2)

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

            _pesoSC01  = config?.PesoErgo           ?? 3;
            _pesoSC06  = config?.PesoTiempos        ?? 2;
            _pesoSC09  = config?.PesoAlmuerzo       ?? 3;
            _pesoSCBAL = config?.PesoBalanceSemanas ?? 2;
        }

        public decimal Evaluar(CromosomaHorario c)
        {
            var spansA = SpansPorDocenteDia(c.Start);
            var spansB = SpansPorDocenteDia(c.StartB);

            decimal fitness = 0;
            fitness += _pesoSC01  * (SC01_HuecosOciosos(spansA) + SC01_HuecosOciosos(spansB));
            fitness += _pesoSC06  * (SC06_BalanceEntreDias(c.Start) + SC06_BalanceEntreDias(c.StartB));
            fitness += _pesoSC09  * (SC09_HorasSeguidas(spansA) + SC09_HorasSeguidas(spansB));
            fitness += _pesoSCBAL * SCBAL_DesbalanceEntreSemanas(c);
            fitness += PesoFactibilidadSalas * GuardaCapacidadAulas(c);
            return fitness;
        }

        // ── Agrupa los genes de cada docente por día, como spans [start, start+dur) en bloques. ──
        private Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> SpansPorDocenteDia(int[] starts)
        {
            var mapa = new Dictionary<Guid, Dictionary<DiaDeSemana, List<(int, int)>>>();
            for (int i = 0; i < starts.Length; i++)
            {
                int start = starts[i];
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

        // Carga real (horas) por docente y día, para el arreglo de inicios de una semana dada.
        private Dictionary<Guid, Dictionary<DiaDeSemana, decimal>> CargaPorDocenteDia(int[] starts)
        {
            var mapa = new Dictionary<Guid, Dictionary<DiaDeSemana, decimal>>();
            for (int i = 0; i < starts.Length; i++)
            {
                int start = starts[i];
                if (start < 0 || start >= _bloques.Count) continue;
                var docente = _sesiones[i].DocenteId;
                var dia = _diaPorIdx[start];

                if (!mapa.TryGetValue(docente, out var porDia)) { porDia = new(); mapa[docente] = porDia; }
                porDia.TryGetValue(dia, out var actual);
                porDia[dia] = actual + _sesiones[i].DuracionHoras;
            }
            return mapa;
        }

        // ③ SC-06: desviación media absoluta de la carga del docente sobre sus días DISPONIBLES.
        private int SC06_BalanceEntreDias(int[] starts)
        {
            var cargaPorDocenteDia = CargaPorDocenteDia(starts);

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

        // ④ SC-BAL: por docente y día, |carga(Semana A) − carga(Semana B)|. TipoA/TipoB nunca
        // contribuyen (StartB==Start por construcción): solo SinAlternancia puede introducir o
        // resolver desbalance entre semanas, aprovechando la libertad de ALT-06.
        private int SCBAL_DesbalanceEntreSemanas(CromosomaHorario c)
        {
            var cargaA = CargaPorDocenteDia(c.Start);
            var cargaB = CargaPorDocenteDia(c.StartB);

            decimal penalizacion = 0;
            foreach (var docente in cargaA.Keys.Union(cargaB.Keys))
            {
                var diasA = cargaA.TryGetValue(docente, out var da) ? da : new Dictionary<DiaDeSemana, decimal>();
                var diasB = cargaB.TryGetValue(docente, out var db) ? db : new Dictionary<DiaDeSemana, decimal>();

                foreach (var dia in diasA.Keys.Union(diasB.Keys))
                {
                    decimal cargaDiaA = diasA.TryGetValue(dia, out var va) ? va : 0m;
                    decimal cargaDiaB = diasB.TryGetValue(dia, out var vb) ? vb : 0m;
                    penalizacion += Math.Abs(cargaDiaA - cargaDiaB);
                }
            }
            return (int)Math.Ceiling(penalizacion);
        }

        // Guarda de aulas: máx. sesiones presenciales simultáneas por (semana, día) vs nº de aulas.
        private int GuardaCapacidadAulas(CromosomaHorario c)
        {
            int exceso = 0;
            foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
            {
                var starts = semana == SemanaAcademica.A ? c.Start : c.StartB;

                // Spans presenciales en esta semana, agrupados por día.
                var porDia = new Dictionary<DiaDeSemana, List<(int start, int dur)>>();
                for (int i = 0; i < c.CantidadGenes; i++)
                {
                    if (ModalidadSemanal.Derivar(_sesiones[i], semana) != Modalidad.Presencial) continue;
                    int start = starts[i];
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
