using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Operadores genéticos del modelo bi-semanal presencial-first. Cada sesión tiene dos genes de
    /// inicio: <c>Start</c> (Semana A) y <c>StartB</c> (Semana B). Invariantes preservados por
    /// construcción:
    ///   I1: todo inicio elegido pertenece a <see cref="_startsValidos"/>[i] = cabe-en-día ∩
    ///       franja disponible del grupo (HC-G01) ∩ ventana horaria de la asignatura (HC-VH) —
    ///       calculado por <see cref="CalculadorDominioSesion"/>, la misma fuente que CP-SAT.
    ///       El docente NO restringe el dominio. Mutación, cruce y perturbación solo eligen de
    ///       ahí, para AMBOS genes. Si el dominio de una sesión queda vacío, su gen se CONGELA
    ///       en el valor semilla de Fase 2 (ningún operador lo mueve): nunca se abre el dominio
    ///       completo, porque eso podría violar HC-G01/HC-VH.
    ///   Regla 9 / ALT-05: para TipoA/TipoB, <c>StartB[i] == Start[i]</c> siempre — los operadores
    ///       solo mueven StartB de forma independiente para SinAlternancia (ALT-06);
    ///       <see cref="Reparar"/> re-sincroniza incondicionalmente el resto en cada llamada.
    /// Los aulas (HC-S01) se resuelven en un pase posterior (<see cref="AsignadorEspacios"/>), no aquí.
    /// </summary>
    public class OperadoresGeneticos
    {
        private readonly Random _rng;
        private readonly List<Sesion> _sesiones;
        private readonly DiaDeSemana[] _diaPorIdx;
        private readonly int[] _duraciones;
        private readonly int[][] _startsValidos;
        private readonly bool[] _esIndependiente; // true = SinAlternancia (ALT-06: StartB libre)
        private readonly int[] _parejaIdx; // M4: índice de la sesión pareada por HC-ALT, o -1

        public OperadoresGeneticos(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            IReadOnlyList<Docente> docentes,
            Random rng,
            IReadOnlyList<Grupo>? grupos = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            IReadOnlySet<Guid>? sesionesFijasIds = null)
        {
            _sesiones  = sesiones;
            _rng       = rng;
            _diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var rangos = BloquesPlanner.RangosPorDia(bloques);

            // HC-G01: índice de disponibilidad por GrupoId (misma fuente que CP-SAT Fase 2).
            var bloquesPermitidosPorGrupo = new Dictionary<Guid, HashSet<int>>();
            if (grupos != null)
            {
                foreach (var grupo in grupos)
                {
                    if (grupo.Id == Guid.Empty) continue;
                    var permitidos = CalculadorDominioSesion.BloquesPermitidos(bloques, grupo.ObtenerDisponibilidadSemanal());
                    if (permitidos is not null)
                        bloquesPermitidosPorGrupo[grupo.Id] = permitidos;
                }
            }

            _duraciones      = new int[sesiones.Count];
            _startsValidos   = new int[sesiones.Count][];
            _esIndependiente = new bool[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
            {
                _duraciones[i]      = Math.Max(1, (int)Math.Ceiling(sesiones[i].DuracionHoras));
                _esIndependiente[i] = sesiones[i].Alternancia == TipoAlternancia.SinAlternancia;

                // Regla 8 (horario base): las sesiones fijas NO se mueven — dominio vacío = gen
                // congelado en la franja que Fase 2 fijó por igualdad.
                if (sesionesFijasIds?.Contains(sesiones[i].Id) == true)
                {
                    _startsValidos[i] = Array.Empty<int>();
                    continue;
                }

                HashSet<int>? permGrupo = null;
                if (sesiones[i].GrupoId.HasValue)
                    bloquesPermitidosPorGrupo.TryGetValue(sesiones[i].GrupoId.Value, out permGrupo);

                (TimeOnly? min, TimeOnly? max) ventana = default;
                ventanaPorAsignatura?.TryGetValue(sesiones[i].AsignaturaId, out ventana);

                // Dominio = cabe-en-día ∩ HC-G01 ∩ HC-VH. Si queda vacío, el gen se congela en
                // su valor actual (la semilla de Fase 2, ya factible): ningún operador elige de
                // un arreglo vacío, así que la sesión simplemente no se mueve.
                _startsValidos[i] = CalculadorDominioSesion.StartsPermitidos(
                    _duraciones[i], bloques, rangos, _diaPorIdx,
                    permGrupo, ventana.min, ventana.max);
            }

            // M4: HC-ALT — índice de la sesión pareada por ParejaAlternanciaId (o -1 si no tiene,
            // o si el dato está corrupto: una "pareja" con != 2 miembros no es responsabilidad del
            // GA repararla, el validador post-generación ya la reporta como conflicto de datos).
            _parejaIdx = Enumerable.Repeat(-1, sesiones.Count).ToArray();
            var indicesPorPareja = new Dictionary<Guid, List<int>>();
            for (int i = 0; i < sesiones.Count; i++)
            {
                if (sesiones[i].ParejaAlternanciaId is not Guid pid) continue;
                if (!indicesPorPareja.TryGetValue(pid, out var lista)) { lista = new(); indicesPorPareja[pid] = lista; }
                lista.Add(i);
            }
            foreach (var lista in indicesPorPareja.Values)
                if (lista.Count == 2)
                {
                    _parejaIdx[lista[0]] = lista[1];
                    _parejaIdx[lista[1]] = lista[0];
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

        // ── Reparación HC-C01: sin solapes de cohorte, en dos pasadas (CR-08: HC-I01, el ──
        // solape de docente, salió del pipeline y quedó subsumido por HC-C01) ────────────
        // Pasada A: idéntica al comportamiento de Incremento 1, sobre Start[]. El resultado se
        // refleja incondicionalmente a StartB para TipoA/TipoB (regla 9: misma franja en ambas
        // semanas). Pasada B: solo repara StartB de SinAlternancia; TipoA/TipoB entran ya
        // colocados como obstáculos fijos (su franja de Semana B es idéntica a la de Semana A,
        // ya resuelta en la pasada A, así que dos genes ligados nunca pueden chocar en B sin
        // haber chocado ya en A).
        public void Reparar(CromosomaHorario cromosoma)
        {
            // Un gen con dominio vacío (sesión fija del horario base o dominio infactible) es un
            // obstáculo: se registra primero y los demás se reparan alrededor de él.
            RepararSemana(cromosoma.Start, movible: i => _startsValidos[i].Length > 0);
            RepararSeparacionDias(cromosoma.Start, movible: i => _startsValidos[i].Length > 0);

            for (int i = 0; i < cromosoma.CantidadGenes; i++)
                if (!_esIndependiente[i])
                    cromosoma.StartB[i] = cromosoma.Start[i];

            RepararSemana(cromosoma.StartB, movible: i => _esIndependiente[i] && _startsValidos[i].Length > 0);
            RepararSeparacionDias(cromosoma.StartB, movible: i => _esIndependiente[i] && _startsValidos[i].Length > 0);
        }

        private void RepararSemana(int[] starts, Func<int, bool> movible)
        {
            // CR-08: la reparación de solapes es por cohorte (GrupoId), no por docente.
            var colocadosPorGrupo = new Dictionary<Guid, List<(int start, int dur)>>();

            // 1) Genes fijos primero: solo registran su ocupación (no se mueven). Necesario para
            //    que los genes movibles, procesados después, vean el panorama completo y no
            //    elijan un inicio que ya choca con un gen fijo que aún no se había registrado.
            for (int i = 0; i < starts.Length; i++)
            {
                if (movible(i)) continue;
                if (!_sesiones[i].GrupoId.HasValue) continue;
                var grupo = _sesiones[i].GrupoId.Value;
                ObtenerLista(colocadosPorGrupo, grupo).Add((starts[i], _duraciones[i]));
            }

            // 2) Genes movibles, en orden. HC-ALT (M4): una pareja de alternancia
            //    (ParejaAlternanciaId) se coloca como una sola unidad, en el MISMO start — si se
            //    procesaran por separado, el segundo miembro podría desincronizarse del primero al
            //    resolver su propio HC-C01 (los dos miembros suelen ser de grupos distintos, cada
            //    uno con su propia ocupación).
            var procesado = new bool[starts.Length];
            for (int i = 0; i < starts.Length; i++)
            {
                if (procesado[i] || !movible(i)) continue;
                procesado[i] = true;
                if (!_sesiones[i].GrupoId.HasValue) continue;

                int j = _parejaIdx[i];
                if (j >= 0 && !procesado[j] && movible(j) && _sesiones[j].GrupoId.HasValue)
                {
                    procesado[j] = true;
                    ColocarPar(i, j, starts, colocadosPorGrupo);
                }
                else
                {
                    ColocarIndividual(i, starts, colocadosPorGrupo);
                }
            }
        }

        // Coloca un gen movible individual: si su start actual choca con lo ya ocupado de su
        // grupo, busca uno libre en su propio dominio; si no hay ninguno libre, lo deja tal cual
        // (mejor esfuerzo — el post-chequeo del orquestador (HC-C01) lo detecta y hace fallback a
        // Fase 2, nunca se publica un horario inválido).
        private void ColocarIndividual(
            int i, int[] starts, Dictionary<Guid, List<(int start, int dur)>> colocadosPorGrupo)
        {
            var grupo = _sesiones[i].GrupoId!.Value;
            int start = starts[i];
            int dur   = _duraciones[i];
            var lista = ObtenerLista(colocadosPorGrupo, grupo);

            bool solapa = lista.Any(p => BloquesPlanner.Solapan(p.start, p.dur, start, dur, _diaPorIdx));
            if (solapa)
            {
                var nuevo = BuscarStartLibre(i, lista);
                if (nuevo.HasValue) start = nuevo.Value;
            }

            starts[i] = start;
            lista.Add((start, dur));
        }

        // HC-ALT: coloca ambos miembros de una pareja de alternancia en el MISMO start, libre en
        // la ocupación de LOS DOS grupos a la vez (SonParejaCompatible ya garantiza duraciones
        // iguales entre pareja). Preferencia: conservar el start actual de i si sigue sirviendo
        // para ambos; si no, buscar en el dominio común (∩ de _startsValidos); si ninguno sirve,
        // deja los starts tal cual — mismo criterio "mejor esfuerzo" que ColocarIndividual, el
        // post-chequeo HC-ALT lo detecta.
        private void ColocarPar(
            int i, int j, int[] starts, Dictionary<Guid, List<(int start, int dur)>> colocadosPorGrupo)
        {
            var grupoI = _sesiones[i].GrupoId!.Value;
            var grupoJ = _sesiones[j].GrupoId!.Value;
            var listaI = ObtenerLista(colocadosPorGrupo, grupoI);
            var listaJ = grupoJ == grupoI ? listaI : ObtenerLista(colocadosPorGrupo, grupoJ);
            int dur = _duraciones[i];

            bool Libre(int cand) =>
                !listaI.Any(p => BloquesPlanner.Solapan(p.start, p.dur, cand, dur, _diaPorIdx)) &&
                (grupoJ == grupoI || !listaJ.Any(p => BloquesPlanner.Solapan(p.start, p.dur, cand, dur, _diaPorIdx)));

            int start;
            if (starts[i] == starts[j] && Libre(starts[i]))
            {
                start = starts[i];
            }
            else
            {
                var comunes = Array.FindAll(_startsValidos[i], s => Array.IndexOf(_startsValidos[j], s) >= 0);
                start = -1;
                foreach (var cand in comunes)
                    if (Libre(cand)) { start = cand; break; }

                if (start == -1)
                    start = comunes.Length > 0 ? comunes[_rng.Next(comunes.Length)] : starts[i];
            }

            starts[i] = start;
            starts[j] = start;
            listaI.Add((start, dur));
            if (grupoJ != grupoI) listaJ.Add((start, dur));
        }

        // HC-SEP: separación mínima de 2 días entre sesiones semanales del mismo (grupo,
        // asignatura, tipo de sesión) — misma regla que CP-SAT/validador
        // (ReglasSesion.SeparacionDiasOk). Procesa cada sesión de la clase en orden y, si su día
        // actual choca con alguna ya colocada, busca en su propio dominio un start que separe de
        // TODAS las anteriores Y siga libre en la ocupación de su grupo (reconstruida tras
        // RepararSemana) — mismo patrón "solo mira hacia atrás" que RepararSemana, sin necesidad
        // de reprocesar en varias pasadas. Las sesiones pareadas (HC-ALT) nunca se reubican aquí
        // — moverlas rompería el bloque compartido que ColocarPar ya fijó — pero sí cuentan como
        // "día ocupado" para sus compañeras de clase sin pareja.
        private void RepararSeparacionDias(int[] starts, Func<int, bool> movible)
        {
            var porClase = new Dictionary<(Guid grupo, Guid asig, TipoSesion tipo), List<int>>();
            for (int i = 0; i < starts.Length; i++)
            {
                if (!_sesiones[i].GrupoId.HasValue) continue;
                var clave = (_sesiones[i].GrupoId!.Value, _sesiones[i].AsignaturaId,
                    CalculadorEspaciosSesion.TipoSesionDe(_sesiones[i]));
                if (!porClase.TryGetValue(clave, out var lista)) { lista = new(); porClase[clave] = lista; }
                lista.Add(i);
            }

            var colocadosPorGrupo = new Dictionary<Guid, List<(int idx, int start, int dur)>>();
            for (int i = 0; i < starts.Length; i++)
            {
                if (!_sesiones[i].GrupoId.HasValue) continue;
                var grupo = _sesiones[i].GrupoId.Value;
                if (!colocadosPorGrupo.TryGetValue(grupo, out var l)) { l = new(); colocadosPorGrupo[grupo] = l; }
                l.Add((i, starts[i], _duraciones[i]));
            }

            foreach (var indices in porClase.Values)
            {
                if (indices.Count < 2) continue;
                var diasColocados = new List<DiaDeSemana>();

                foreach (var i in indices)
                {
                    var dia = _diaPorIdx[starts[i]];
                    if (diasColocados.All(d => ReglasSesion.SeparacionDiasOk(dia, d)))
                    {
                        diasColocados.Add(dia);
                        continue;
                    }

                    if (movible(i) && _parejaIdx[i] < 0)
                    {
                        var ocupados = colocadosPorGrupo[_sesiones[i].GrupoId!.Value];
                        ocupados.RemoveAll(p => p.idx == i);

                        var nuevo = BuscarStartSeparado(i, ocupados, diasColocados);
                        if (nuevo.HasValue)
                        {
                            starts[i] = nuevo.Value;
                            dia = _diaPorIdx[nuevo.Value];
                        }
                        ocupados.Add((i, starts[i], _duraciones[i]));
                    }
                    // Sin movible o pareada: se deja tal cual (mejor esfuerzo). El post-chequeo
                    // HC-SEP lo detecta y el orquestador hace fallback a Fase 2.

                    diasColocados.Add(dia);
                }
            }
        }

        private int? BuscarStartSeparado(
            int gen, List<(int idx, int start, int dur)> ocupados, List<DiaDeSemana> diasAEvitar)
        {
            var validos = _startsValidos[gen];
            if (validos.Length == 0) return null;
            int dur = _duraciones[gen];

            foreach (var cand in validos)
            {
                var dia = _diaPorIdx[cand];
                if (!diasAEvitar.All(d => ReglasSesion.SeparacionDiasOk(dia, d))) continue;
                if (ocupados.Any(o => BloquesPlanner.Solapan(o.start, o.dur, cand, dur, _diaPorIdx))) continue;
                return cand;
            }
            return null;
        }

        private static List<(int start, int dur)> ObtenerLista(
            Dictionary<Guid, List<(int start, int dur)>> mapa, Guid clave)
        {
            if (!mapa.TryGetValue(clave, out var lista))
            {
                lista = new List<(int, int)>();
                mapa[clave] = lista;
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
