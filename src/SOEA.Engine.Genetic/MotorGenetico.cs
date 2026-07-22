using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Motor de Algoritmo Genético (Fase 3) del modelo bi-semanal presencial-first.
    /// Optimiza objetivos blandos de la cohorte (huecos, &gt; 6 horas seguidas, balance entre días,
    /// balance entre semanas SC-BAL) moviendo el inicio de cada sesión por semana:
    /// <c>Start</c> (Semana A) y <c>StartB</c> (Semana B). Para TipoA/TipoB ambos coinciden
    /// siempre (regla 9 / ALT-05); para SinAlternancia pueden diferir (ALT-06).
    /// Las restricciones duras se preservan así:
    ///   - HC-G01 (disponibilidad de grupo): los operadores solo eligen inicios en la franja
    ///     declarada por el grupo (Matutino / Vespertino). El docente NO restringe el dominio.
    ///   - HC-S01/S03 (aulas): pase determinista posterior con los inicios ya fijados, por semana.
    ///   - HC-I03 (horas semanales): invariante (el GA no cambia duraciones; docente se asigna post-gen).
    /// Si el mejor cromosoma no es factible (aulas no asignables), hace FALLBACK a las asignaciones
    /// de Fase 2 — nunca devuelve un horario peor o inválido.
    /// </summary>
    public class MotorGenetico : IMotorGenetico
    {
        private readonly ILogger<MotorGenetico> _logger;
        private const int TamañoTorneo = 5;

        public MotorGenetico(ILogger<MotorGenetico> logger) => _logger = logger;

        public Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion>            sesiones,
            IEnumerable<AsignacionSemanal> asignacionesFase2,
            IEnumerable<BloqueTiempo>      bloques,
            IEnumerable<Espacio>           espacios,
            IEnumerable<Docente>           docentes,
            IEnumerable<Grupo>?            grupos = null,
            ConfiguracionOptimizacion?     config = null,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            IReadOnlySet<Guid>?            sesionesFijasIds = null,
            IReadOnlyList<Guid>?           sesionesCedidasParaRevertir = null,
            CancellationToken              ct = default)
        {
            var s  = sesiones.ToList();
            var a2 = asignacionesFase2.ToList();
            var b  = bloques.ToList();
            var e  = espacios.ToList();
            var d  = docentes.ToList();
            var g  = grupos?.ToList();
            var c  = config ?? new ConfiguracionOptimizacion();
            var ia = infoAsignatura ?? new Dictionary<Guid, (int, CategoriaAsignatura)>();
            return Task.Run(() => OptimizarSincrono(s, a2, b, e, d, g, c, ia, ventanaPorAsignatura, sesionesFijasIds, sesionesCedidasParaRevertir, ct), ct);
        }

        private ResultadoOptimizacion OptimizarSincrono(
            List<Sesion>            sesiones,
            List<AsignacionSemanal> asignacionesFase2,
            List<BloqueTiempo>      bloques,
            List<Espacio>           espacios,
            List<Docente>           docentes,
            List<Grupo>?            grupos,
            ConfiguracionOptimizacion config,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)> infoAsignatura,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura,
            IReadOnlySet<Guid>?     sesionesFijasIds,
            IReadOnlyList<Guid>?    sesionesCedidasParaRevertir,
            CancellationToken ct)
        {
            if (sesiones.Count == 0 || bloques.Count == 0)
            {
                _logger.LogWarning("Fase 3: sin sesiones o bloques; se devuelven las asignaciones de Fase 2.");
                return new ResultadoOptimizacion(asignacionesFase2, 0, 0, UsoFallback: true);
            }

            var tamañoPoblacion    = Math.Max(10, config.TamañoPoblacion);
            var maxGeneraciones    = Math.Max(1,  config.MaxGeneraciones);
            var probMutacion       = Math.Clamp(config.ProbabilidadMutacion, 0.0, 1.0);
            var probCruce          = Math.Clamp(config.ProbabilidadCruce,    0.0, 1.0);
            var umbralConvergencia = Math.Max(1,  config.UmbralConvergencia);

            // RNG inyectable: semilla fija = reproducible (tests); null = aleatorio (producción).
            var rng = config.Semilla.HasValue ? new Random(config.Semilla.Value) : new Random();

            var bloqueIndex = Enumerable.Range(0, bloques.Count).ToDictionary(i => bloques[i].Id, i => i);

            var diaPorIdx  = BloquesPlanner.DiaPorBloqueIdx(bloques);
            var duraciones = sesiones.Select(s => Math.Max(1, (int)Math.Ceiling(s.DuracionHoras))).ToArray();

            // ── Semilla: inicios desde Fase 2, Semana A en Start y Semana B en StartB ─────
            var esIndependiente = sesiones.Select(s => s.Alternancia == TipoAlternancia.SinAlternancia).ToArray();
            var startSemilla  = SembrarInicios(sesiones, asignacionesFase2, bloqueIndex);
            var startBSemilla = SembrarInicioB(sesiones, asignacionesFase2, bloqueIndex, startSemilla, esIndependiente);
            var sesionIds = sesiones.Select(s => s.Id).ToArray();
            var semilla = new CromosomaHorario(sesionIds, startSemilla, startBSemilla);

            var operadores = new OperadoresGeneticos(sesiones, bloques, docentes, rng, grupos,
                ventanaPorAsignatura, sesionesFijasIds);
            var evaluador  = new EvaluadorFitness(sesiones, bloques, docentes, espacios, config, infoAsignatura);

            _logger.LogInformation("Fase 3 (Genético): {S} sesiones, población={P}, maxGen={G}.",
                sesiones.Count, tamañoPoblacion, maxGeneraciones);

            // ── Población inicial (semilla + perturbaciones reparadas) ────────────────────
            var poblacion = new List<(CromosomaHorario cromosoma, decimal fitness)>
            {
                (semilla, evaluador.Evaluar(semilla))
            };
            for (int i = 1; i < tamañoPoblacion; i++)
            {
                var perturbado = operadores.ClonarYPerturbar(semilla, 2 + rng.Next(3));
                operadores.Reparar(perturbado);
                poblacion.Add((perturbado, evaluador.Evaluar(perturbado)));
            }

            decimal mejorFitness = poblacion.Min(p => p.fitness);
            int sinMejora = 0, generacionFinal = 0;

            // ── Ciclo generacional (μ+λ) con elitismo ─────────────────────────────────────
            // Cada generación produce TamañoPoblacion hijos (no uno solo); se unen a los padres
            // y sobreviven los TamañoPoblacion mejores. El elitismo garantiza que mejorFitness es
            // monótono no-creciente: nunca se pierde el mejor cromosoma visto. Con la config por
            // defecto (50 × 200) esto da 10.000 evaluaciones en vez de las ≤200 del steady-state
            // anterior (1 hijo/generación) — el cuello de botella era presupuesto, no algoritmo.
            for (int gen = 1; gen <= maxGeneraciones; gen++)
            {
                ct.ThrowIfCancellationRequested();

                var hijos = new List<(CromosomaHorario cromosoma, decimal fitness)>(tamañoPoblacion);
                for (int i = 0; i < tamañoPoblacion; i++)
                {
                    var p1 = operadores.SeleccionTorneo(poblacion, TamañoTorneo);
                    var p2 = operadores.SeleccionTorneo(poblacion, TamañoTorneo);
                    var hijo = operadores.Cruce(p1, p2, probCruce);
                    operadores.Mutar(hijo, probMutacion);
                    operadores.Reparar(hijo);
                    hijos.Add((hijo, evaluador.Evaluar(hijo)));
                }

                poblacion = poblacion.Concat(hijos)
                    .OrderBy(p => p.fitness)
                    .Take(tamañoPoblacion)
                    .ToList();

                var mejorGen = poblacion[0].fitness; // ya ordenada: el primero es el mínimo
                if (mejorGen < mejorFitness) { mejorFitness = mejorGen; sinMejora = 0; }
                else sinMejora++;

                generacionFinal = gen;
                if (sinMejora >= umbralConvergencia)
                {
                    _logger.LogInformation("Convergencia en gen {G}. Fitness={F}.", gen, mejorFitness);
                    break;
                }
            }

            var mejor = poblacion.MinBy(p => p.fitness).cromosoma;

            // SC-PRES informativo: constante para el conjunto de sesiones del run (ver EvaluadorFitness).
            var penalizacionPresencial = evaluador.PenalizacionPresencial;

            // ── Verificación HC-C01 + asignación de aulas; si falla → fallback a Fase 2 ───
            if (TieneSolapeGrupo(mejor, sesiones, duraciones, diaPorIdx))
            {
                _logger.LogWarning("Fase 3: el mejor cromosoma tiene solape residual de cohorte; fallback a Fase 2.");
                return new ResultadoOptimizacion(asignacionesFase2, mejorFitness, generacionFinal, UsoFallback: true, penalizacionPresencial);
            }

            // HC-CAP: aforo requerido por sesión = estudiantes inscritos de su grupo.
            var estudiantesPorGrupo = (grupos ?? new List<Grupo>())
                .Where(gr => gr.Id != Guid.Empty)
                .GroupBy(gr => gr.Id)
                .ToDictionary(gr => gr.Key, gr => gr.First().EstudiantesInscritos);

            var aulas = AsignadorEspacios.Asignar(
                sesiones, mejor.Start, mejor.StartB, duraciones, espacios, diaPorIdx, estudiantesPorGrupo);
            if (aulas is null)
            {
                _logger.LogWarning("Fase 3: no hay asignación de aulas factible para el mejor cromosoma; fallback a Fase 2.");
                return new ResultadoOptimizacion(asignacionesFase2, mejorFitness, generacionFinal, UsoFallback: true, penalizacionPresencial);
            }

            // ── Pase de reversión post-Fase 3: recuperar a presencial toda sesión cedida por el
            // fallback presencial-first (Etapas 1/2) que sí quepa en el empaque REAL de aulas que
            // dejó el GA — más preciso que el pre-check agregado de Fase 2. Se prueba en orden
            // INVERSO (última cedida primero); cada intento se confirma solo si AsignadorEspacios
            // sigue encontrando una asignación completa para TODAS las sesiones presenciales.
            var sesionesRevertidas = new List<Guid>();
            if (sesionesCedidasParaRevertir is { Count: > 0 })
            {
                var sesionPorId = sesiones.ToDictionary(x => x.Id);
                for (int k = sesionesCedidasParaRevertir.Count - 1; k >= 0; k--)
                {
                    if (!sesionPorId.TryGetValue(sesionesCedidasParaRevertir[k], out var sesionCedida)) continue;
                    if (!sesionCedida.CedidaPorSaturacion || sesionCedida.Bloqueada) continue;

                    var modalidadPrevia   = sesionCedida.Modalidad;
                    var alternanciaPrevia = sesionCedida.Alternancia;
                    var patronPrevio      = sesionCedida.PatronAlternanciaId;

                    if (!sesionCedida.RevertirCesion()) continue;

                    var aulasTentativas = AsignadorEspacios.Asignar(
                        sesiones, mejor.Start, mejor.StartB, duraciones, espacios, diaPorIdx, estudiantesPorGrupo);

                    if (aulasTentativas is not null)
                    {
                        aulas = aulasTentativas;
                        sesionesRevertidas.Add(sesionCedida.Id);
                    }
                    else if (modalidadPrevia == Modalidad.Virtual)
                        sesionCedida.VirtualizarSesion(cedidaPorSaturacion: true);
                    else
                        sesionCedida.AplicarAlternancia(alternanciaPrevia, patronPrevio, cedidaPorSaturacion: true);
                }

                if (sesionesRevertidas.Count > 0)
                    _logger.LogInformation(
                        "Fase 3: {N} sesión(es) revertidas a presencial tras validar aulas reales.", sesionesRevertidas.Count);
            }

            var asignacionesGA = Decodificar(sesiones, mejor, bloques, aulas);
            _logger.LogInformation("Fase 3 completada. Generaciones={G}, Fitness={F}.", generacionFinal, mejorFitness);
            return new ResultadoOptimizacion(asignacionesGA, mejorFitness, generacionFinal, UsoFallback: false, penalizacionPresencial,
                sesionesRevertidas.Count > 0 ? sesionesRevertidas : null);
        }

        // Inicios semilla desde la Semana A de Fase 2 (Start). Regla 9: A y B comparten franja en
        // TipoA/TipoB — ver SembrarInicioB para la Semana B.
        private static int[] SembrarInicios(
            List<Sesion> sesiones,
            List<AsignacionSemanal> asignacionesFase2,
            Dictionary<Guid, int> bloqueIndex)
        {
            var bloquePorSesionA = asignacionesFase2
                .Where(a => a.Semana == SemanaAcademica.A)
                .GroupBy(a => a.SesionId)
                .ToDictionary(g => g.Key, g => g.First().BloqueTiempoId);

            var start = new int[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
            {
                if (bloquePorSesionA.TryGetValue(sesiones[i].Id, out var bid) && bloqueIndex.TryGetValue(bid, out var idx))
                    start[i] = idx;
                else if (bloqueIndex.TryGetValue(sesiones[i].BloqueTiempoId, out var idx2))
                    start[i] = idx2;
                else
                    start[i] = 0;
            }
            return start;
        }

        // Inicios semilla de Semana B (StartB). Para TipoA/TipoB se fuerza == startA (regla 9 /
        // ALT-05), de forma defensiva aunque Fase 2 ya debería traerlos iguales. Para
        // SinAlternancia se siembra desde la Semana B real de Fase 2 (ALT-06: puede diferir).
        private static int[] SembrarInicioB(
            List<Sesion> sesiones,
            List<AsignacionSemanal> asignacionesFase2,
            Dictionary<Guid, int> bloqueIndex,
            int[] startA,
            bool[] esIndependiente)
        {
            var bloquePorSesionB = asignacionesFase2
                .Where(a => a.Semana == SemanaAcademica.B)
                .GroupBy(a => a.SesionId)
                .ToDictionary(g => g.Key, g => g.First().BloqueTiempoId);

            var startB = new int[sesiones.Count];
            for (int i = 0; i < sesiones.Count; i++)
            {
                if (!esIndependiente[i])
                {
                    startB[i] = startA[i];
                    continue;
                }

                if (bloquePorSesionB.TryGetValue(sesiones[i].Id, out var bid) && bloqueIndex.TryGetValue(bid, out var idx))
                    startB[i] = idx;
                else
                    startB[i] = startA[i];
            }
            return startB;
        }

        private static List<AsignacionSemanal> Decodificar(
            List<Sesion> sesiones,
            CromosomaHorario cromosoma,
            List<BloqueTiempo> bloques,
            Dictionary<(Guid, SemanaAcademica), Guid> aulas)
        {
            var asignaciones = new List<AsignacionSemanal>(sesiones.Count * 2);
            for (int i = 0; i < sesiones.Count; i++)
            {
                var sesion = sesiones[i];
                foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
                {
                    var bloque = bloques[semana == SemanaAcademica.A ? cromosoma.Start[i] : cromosoma.StartB[i]];
                    var modalidad = ModalidadSemanal.Derivar(sesion, semana);
                    Guid? espacioId = null;
                    if (modalidad == Modalidad.Presencial &&
                        aulas.TryGetValue((sesion.Id, semana), out var eid))
                        espacioId = eid;

                    asignaciones.Add(new AsignacionSemanal(
                        Guid.NewGuid(), sesion.Id, semana, bloque.Id, espacioId, modalidad));
                }
            }
            return asignaciones;
        }

        private static bool TieneSolapeGrupo(
            CromosomaHorario c, List<Sesion> sesiones, int[] duraciones, DiaDeSemana[] diaPorIdx)
        {
            return TieneSolapeGrupoSemana(c.Start, sesiones, duraciones, diaPorIdx)
                || TieneSolapeGrupoSemana(c.StartB, sesiones, duraciones, diaPorIdx);
        }

        // CR-08: solape por cohorte (GrupoId) — el grupo no puede estar en dos sesiones a la vez.
        private static bool TieneSolapeGrupoSemana(
            int[] starts, List<Sesion> sesiones, int[] duraciones, DiaDeSemana[] diaPorIdx)
        {
            var porGrupo = new Dictionary<Guid, List<(int start, int dur)>>();
            for (int i = 0; i < starts.Length; i++)
            {
                if (!sesiones[i].GrupoId.HasValue) continue;
                var grupo = sesiones[i].GrupoId.Value;
                int start = starts[i], dur = duraciones[i];
                if (!porGrupo.TryGetValue(grupo, out var lista)) { lista = new(); porGrupo[grupo] = lista; }
                if (lista.Any(o => BloquesPlanner.Solapan(o.start, o.dur, start, dur, diaPorIdx)))
                    return true;
                lista.Add((start, dur));
            }
            return false;
        }
    }
}
