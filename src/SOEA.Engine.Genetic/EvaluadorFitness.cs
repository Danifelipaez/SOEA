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
    /// Fitness del horario (menor = mejor). Objetivos temporales centrados en la cohorte
    /// (CR-08: el docente sale del pipeline) — ninguno depende del aula, por eso el aula no está en
    /// el cromosoma:
    ///   ① SC-01: minimizar huecos ociosos entre sesiones de la misma cohorte en un día.
    ///   ② SC-09: penalizar rachas de > 6 horas SEGUIDAS de sesiones (restricción BLANDA fuerte).
    ///   ③ SC-06: balancear la carga de la cohorte entre los días operativos de la grilla (desviación
    ///            media absoluta sobre todos los días, contando ceros).
    /// Cada sesión tiene UN inicio que aplica a todas las semanas (regla 9 / ALT-05), así que
    /// ①②③ se calculan UNA sola vez. Antes se calculaban dos veces, una por semana, y existía un
    /// cuarto término SC-BAL que penalizaba el desbalance de carga entre A y B: ambos solo tenían
    /// sentido cuando una sesión podía caer en franjas distintas según la semana. Con un gen por
    /// sesión ese desbalance es idénticamente cero, así que SC-BAL se eliminó.
    ///
    /// Además, una GUARDA de factibilidad de aulas (no es un objetivo blando): penaliza fuerte si en
    /// alguna (semana, día) hay más sesiones presenciales simultáneas que aulas. Mantiene al GA en la
    /// región donde el pase posterior de asignación de aulas (HC-S01) es factible.
    ///
    /// SC-PRES (ceder presencialidad de sesiones de alta prioridad) NO forma parte de <see cref="Evaluar"/>:
    /// es un término constante para el conjunto de sesiones del run (el GA solo mueve Start,
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
        private readonly int _nEspaciosLab;
        private readonly int _nEspaciosNoLab;

        private readonly int _pesoSC01;  // huecos
        private readonly int _pesoSC06;  // balance entre días
        private readonly int _pesoSC09;  // > _umbralSC09 horas seguidas
        private readonly int _umbralSC09; // C2 auditoría: antes hardcodeado en 6 dentro de SC09_HorasSeguidas
        private readonly int _pesoSCPRES;     // SC-PRES: ceder presencialidad de sesiones de alta prioridad
        private readonly decimal _penalSCPRES; // término SC-PRES precomputado (ver nota)

        public EvaluadorFitness(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Espacio> espacios,
            ConfiguracionOptimizacion? config = null,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null)
        {
            _sesiones  = sesiones;
            _bloques   = bloques;
            _nEspaciosLab   = espacios.Count(e => e.Tipo == TipoEspacio.Laboratorio);
            _nEspaciosNoLab = espacios.Count - _nEspaciosLab;
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

        // ponytail: constante para un conjunto de sesiones dado — el GA solo mueve Start,
        // nunca la alternancia (la fija el bucle de cesión antes de generar). Se precomputa una vez
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
            var spans = SpansPorGrupoDia(c.Start);

            decimal fitness = 0;
            fitness += _pesoSC01 * SC01_HuecosOciosos(spans);
            fitness += _pesoSC06 * SC06_BalanceEntreDias(c.Start);
            fitness += _pesoSC09 * SC09_HorasSeguidas(spans);
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
        private int SC01_HuecosOciosos(Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> spansPorGrupoDia)
        {
            int huecos = 0;
            foreach (var porDia in spansPorGrupoDia.Values)
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
        private int SC09_HorasSeguidas(Dictionary<Guid, Dictionary<DiaDeSemana, List<(int start, int dur)>>> spansPorGrupoDia)
        {
            int penalizacion = 0;
            foreach (var porDia in spansPorGrupoDia.Values)
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

        // Guarda de aulas: máx. sesiones presenciales simultáneas por (semana, día) vs nº de aulas.
        // M1 (auditoría): antes comparaba la concurrencia TOTAL contra TODAS las aulas sin distinguir
        // tipo — un run con muchos salones y ningún laboratorio nunca penalizaba la sobre-reserva de
        // laboratorios (los salones "tapaban" el exceso en el conteo agregado). Ahora separa la
        // concurrencia por clase (Laboratorio vs el resto), misma partición que gobierna la regla
        // por defecto de HC-S03 en CalculadorEspaciosSesion.
        private int GuardaCapacidadAulas(CromosomaHorario c)
        {
            int exceso = 0;
            foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
            {
                var starts = c.Start;

                // Spans que ocupan aula en esta semana, agrupados por día y por clase de espacio.
                // Sigue siendo por semana: lo que no alterna cuenta en las dos, una pareja reparte
                // un miembro en cada una — que es justamente la capacidad que libera emparejar.
                var porDiaLab = new Dictionary<DiaDeSemana, List<(int start, int dur)>>();
                var porDiaNoLab = new Dictionary<DiaDeSemana, List<(int start, int dur)>>();
                for (int i = 0; i < c.CantidadGenes; i++)
                {
                    if (!ModalidadSemanal.SemanasQueOcupanEspacio(_sesiones[i]).Contains(semana)) continue;
                    int start = starts[i];
                    if (start < 0 || start >= _bloques.Count) continue;
                    var dia = _diaPorIdx[start];
                    var porDia = CalculadorEspaciosSesion.TipoSesionDe(_sesiones[i]) == TipoSesion.Laboratorio
                        ? porDiaLab : porDiaNoLab;
                    if (!porDia.TryGetValue(dia, out var lista)) { lista = new(); porDia[dia] = lista; }
                    lista.Add((start, _duraciones[i]));
                }

                foreach (var spans in porDiaLab.Values)
                {
                    int maxConcurrentes = MaxConcurrencia(spans);
                    if (maxConcurrentes > _nEspaciosLab)
                        exceso += maxConcurrentes - _nEspaciosLab;
                }
                foreach (var spans in porDiaNoLab.Values)
                {
                    int maxConcurrentes = MaxConcurrencia(spans);
                    if (maxConcurrentes > _nEspaciosNoLab)
                        exceso += maxConcurrentes - _nEspaciosNoLab;
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
