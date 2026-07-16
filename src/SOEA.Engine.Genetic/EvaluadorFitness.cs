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
    /// Fitness del modelo bi-semanal (menor = mejor). Objetivos temporales centrados en la cohorte
    /// (CR-08: el docente sale del pipeline) — ninguno depende del aula, por eso el aula no está en
    /// el cromosoma:
    ///   ① SC-01: minimizar huecos ociosos entre sesiones de la misma cohorte en un día.
    ///   ② SC-09: penalizar rachas de > 6 horas SEGUIDAS de sesiones (restricción BLANDA fuerte).
    ///   ③ SC-06: balancear la carga de la cohorte entre los días operativos de la grilla (desviación
    ///            media absoluta sobre todos los días, contando ceros).
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
    ///
    /// SC-PRES (ceder presencialidad de sesiones de alta prioridad) NO forma parte de <see cref="Evaluar"/>:
    /// es un término constante para el conjunto de sesiones del run (el GA solo mueve Start/StartB,
    /// nunca la alternancia — eso lo decide Application antes de generar), así que sumarlo al
    /// fitness no cambiaba ningún ranking, solo inflaba el número reportado. Se expone aparte en
    /// <see cref="PenalizacionPresencial"/> como métrica informativa.
    /// </summary>
    public class EvaluadorFitness
    {
        private const int PesoFactibilidadSalas = 1000;

        private readonly List<Sesion> _sesiones;
        private readonly List<BloqueTiempo> _bloques;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly HashSet<DiaDeSemana> _diasGrilla; // días operativos de la grilla (buckets SC-06)
        private readonly int[] _duraciones;
        private readonly int _nEspacios;

        private readonly int _pesoSC01;  // huecos
        private readonly int _pesoSC06;  // balance entre días
        private readonly int _pesoSC09;  // > _umbralSC09 horas seguidas
        private readonly int _umbralSC09; // C2 auditoría: antes hardcodeado en 6 dentro de SC09_HorasSeguidas
        private readonly int _pesoSCBAL; // balance entre semanas A/B (Incremento 2)
        private readonly int _pesoSCPRES;     // SC-PRES: ceder presencialidad de sesiones de alta prioridad
        private readonly decimal _penalSCPRES; // término SC-PRES precomputado (ver nota)

        public EvaluadorFitness(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Docente> docentes,
            List<Espacio> espacios,
            ConfiguracionOptimizacion? config = null,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null)
        {
            _sesiones  = sesiones;
            _bloques   = bloques;
            _nEspacios = espacios.Count;
            _diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            _diasGrilla = _diaPorIdx.Distinct().ToHashSet();

            _duraciones = new int[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
                _duraciones[i] = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));

            // CR-08: el docente sale del pipeline; el eje de ergonomía es la cohorte (GrupoId).
            // El grupo no declara disponibilidad, así que SC-06 balancea sobre los días con carga.

            _pesoSC01  = config?.PesoErgo             ?? 3;
            _pesoSC06  = config?.PesoTiempos          ?? 2;
            _pesoSC09  = config?.PesoMaxHorasSeguidas ?? 3;
            _umbralSC09 = config?.UmbralHorasSeguidas ?? 6;
            _pesoSCBAL = config?.PesoBalanceSemanas   ?? 2;
            _pesoSCPRES = config?.PesoPresencialFirst ?? 4;
            _penalSCPRES = PenalizacionPresencialFirst(sesiones, infoAsignatura);
        }

        /// <summary>
        /// SC-PRES informativo (no entra en <see cref="Evaluar"/>, ver comentario de clase):
        /// cuánto pesa que sesiones de alta prioridad hayan cedido presencialidad, proporcional a
        /// su prioridad — sesión única (1/sem) u Obligatoria pesan más que la 2ª sesión de una
        /// materia con 2/sem o una Electiva.
        /// </summary>
        public decimal PenalizacionPresencial => _pesoSCPRES * _penalSCPRES;

        // ponytail: constante para un conjunto de sesiones dado — el GA solo mueve Start/StartB,
        // nunca la alternancia (fijada antes en AplicarPrioridadPresencial). Se precomputa una vez
        // aquí en vez de recalcularse por cromosoma.
        private static decimal PenalizacionPresencialFirst(
            List<Sesion> sesiones,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? info)
        {
            if (info is null || info.Count == 0) return 0m;

            decimal total = 0m;
            foreach (var s in sesiones)
            {
                if (s.Alternancia == TipoAlternancia.SinAlternancia) continue; // mantiene presencialidad plena
                if (!info.TryGetValue(s.AsignaturaId, out var meta)) continue;

                int pesoEstructural = meta.sesionesSemana <= 1 ? 3 : 1; // única = último recurso ⇒ peor
                int pesoCategoria = meta.categoria switch
                {
                    CategoriaAsignatura.Obligatoria => 3,
                    CategoriaAsignatura.Optativa    => 2,
                    _                               => 1   // Electiva: cede con la menor penalización
                };
                total += pesoEstructural * pesoCategoria;
            }
            return total;
        }

        public decimal Evaluar(CromosomaHorario c)
        {
            var spansA = SpansPorGrupoDia(c.Start);
            var spansB = SpansPorGrupoDia(c.StartB);

            decimal fitness = 0;
            fitness += _pesoSC01  * (SC01_HuecosOciosos(spansA) + SC01_HuecosOciosos(spansB));
            fitness += _pesoSC06  * (SC06_BalanceEntreDias(c.Start) + SC06_BalanceEntreDias(c.StartB));
            fitness += _pesoSC09  * (SC09_HorasSeguidas(spansA) + SC09_HorasSeguidas(spansB));
            fitness += _pesoSCBAL * SCBAL_DesbalanceEntreSemanas(c);
            fitness += PesoFactibilidadSalas * GuardaCapacidadAulas(c);
            return fitness;
        }

        // ── Agrupa los genes de cada grupo (cohorte) por día, como spans [start, start+dur). ──
        private Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> SpansPorGrupoDia(int[] starts)
        {
            var mapa = new Dictionary<Guid, Dictionary<DiaDeSemana, List<(int, int)>>>();
            for (int i = 0; i < starts.Length; i++)
            {
                int start = starts[i];
                if (start < 0 || start >= _bloques.Count) continue;
                if (!_sesiones[i].GrupoId.HasValue) continue; // CR-08: la ergonomía se mide por cohorte
                var grupo = _sesiones[i].GrupoId.Value;
                var dia = _diaPorIdx[start];

                if (!mapa.TryGetValue(grupo, out var porDia)) { porDia = new(); mapa[grupo] = porDia; }
                if (!porDia.TryGetValue(dia, out var lista)) { lista = new(); porDia[dia] = lista; }
                lista.Add((start, _duraciones[i]));
            }
            return mapa;
        }

        // ① SC-01: suma de huecos (en horas) entre sesiones consecutivas de la cohorte por día.
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

        // ② SC-09: por cada racha contigua de sesiones, penaliza las horas que excedan _umbralSC09.
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
                            penalizacion += Math.Max(0, (rachaFin!.Value - rachaInicio.Value) - _umbralSC09);
                            rachaInicio = start; rachaFin = start + dur;
                        }
                    }
                    if (rachaInicio is not null)
                        penalizacion += Math.Max(0, (rachaFin!.Value - rachaInicio.Value) - _umbralSC09);
                }
            return penalizacion;
        }

        // Carga real (horas) por grupo (cohorte) y día, para el arreglo de inicios de una semana dada.
        private Dictionary<Guid, Dictionary<DiaDeSemana, decimal>> CargaPorGrupoDia(int[] starts)
        {
            var mapa = new Dictionary<Guid, Dictionary<DiaDeSemana, decimal>>();
            for (int i = 0; i < starts.Length; i++)
            {
                int start = starts[i];
                if (start < 0 || start >= _bloques.Count) continue;
                if (!_sesiones[i].GrupoId.HasValue) continue; // CR-08: la carga se mide por cohorte
                var grupo = _sesiones[i].GrupoId.Value;
                var dia = _diaPorIdx[start];

                if (!mapa.TryGetValue(grupo, out var porDia)) { porDia = new(); mapa[grupo] = porDia; }
                porDia.TryGetValue(dia, out var actual);
                porDia[dia] = actual + _sesiones[i].DuracionHoras;
            }
            return mapa;
        }

        // ③ SC-06: desviación media absoluta de la carga de la cohorte sobre los días operativos
        // de la grilla (incluye ceros). El grupo no declara disponibilidad, así que se busca
        // repartir su carga entre los días en que la institución opera (penaliza concentrarla).
        private int SC06_BalanceEntreDias(int[] starts)
        {
            var cargaPorGrupoDia = CargaPorGrupoDia(starts);

            decimal penalizacion = 0;
            foreach (var (grupo, cargaDia) in cargaPorGrupoDia)
            {
                var diasBucket = _diasGrilla;
                if (diasBucket.Count <= 1) continue;

                decimal total = cargaDia.Values.Sum();
                decimal media = total / diasBucket.Count;
                decimal mad = diasBucket.Sum(d => Math.Abs((cargaDia.TryGetValue(d, out var l) ? l : 0m) - media));
                penalizacion += mad;
            }
            return (int)Math.Ceiling(penalizacion);
        }

        // ④ SC-BAL: por cohorte y día, |carga(Semana A) − carga(Semana B)|. TipoA/TipoB nunca
        // contribuyen (StartB==Start por construcción): solo SinAlternancia puede introducir o
        // resolver desbalance entre semanas, aprovechando la libertad de ALT-06.
        private int SCBAL_DesbalanceEntreSemanas(CromosomaHorario c)
        {
            var cargaA = CargaPorGrupoDia(c.Start);
            var cargaB = CargaPorGrupoDia(c.StartB);

            decimal penalizacion = 0;
            foreach (var grupo in cargaA.Keys.Union(cargaB.Keys))
            {
                var diasA = cargaA.TryGetValue(grupo, out var da) ? da : new Dictionary<DiaDeSemana, decimal>();
                var diasB = cargaB.TryGetValue(grupo, out var db) ? db : new Dictionary<DiaDeSemana, decimal>();

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
