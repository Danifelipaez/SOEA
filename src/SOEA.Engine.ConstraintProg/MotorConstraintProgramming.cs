using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;
using CpDomain = Google.OrTools.Util.Domain;
#pragma warning disable CA1860 // Evitar usar el método 'Contains' de Enumerable -- se usa HashSet<int>

namespace SOEA.Engine.ConstraintProg
{
    /// <summary>
    /// Fase 2 — Constraint Programming con OR-Tools CP-SAT.
    ///
    /// Modela cada sesión como UN intervalo de longitud fija = DuracionHoras (regla 9 / ALT-05:
    /// la franja y el aula son un dato único que aplica a todas las semanas del semestre; solo la
    /// modalidad alterna). Antes había dos intervalos por sesión, uno por semana, y nada obligaba
    /// a que coincidieran salvo para TipoA/TipoB — de ahí salían dos horarios incompatibles.
    ///
    /// El único eje sensible a la semana es el AULA, porque una sesión virtual la libera:
    ///   - HC-C01 (cohorte) — un NoOverlap por grupo, independiente de la semana: una sesión que
    ///     alterna se sigue dictando virtualmente la semana contraria y consume el tiempo del grupo.
    ///   - HC-S01 (espacio) — NoOverlap por (espacio, semana), donde cada sesión aporta su intervalo
    ///     a las semanas que devuelve <see cref="ModalidadSemanal.SemanasQueOcupanEspacio"/>: ambas
    ///     si no alterna, solo la suya si es TipoA/TipoB. Por eso una pareja comparte legalmente
    ///     bloque y aula, y por eso emparejar es lo único que libera capacidad.
    /// CR-08: el docente sale del pipeline. La duración es inmutable (CLAUDE.md regla 6).
    /// </summary>
    public class MotorConstraintProgramming : IMotorConstraintProgramming
    {
        private readonly ILogger<MotorConstraintProgramming> _logger;
        private readonly CpSatOptions _options;

        private static readonly SemanaAcademica[] Semanas = { SemanaAcademica.A, SemanaAcademica.B };

        /// <summary>
        /// A partir de este % de la capacidad de espacios, el modelo puede ser factible pero
        /// agotar el timeout por saturación (muchas sesiones compitiendo por pocos huecos).
        /// Solo dispara un LogWarning — no cambia el rechazo por demanda &gt; capacidad.
        /// </summary>
        private const decimal UmbralSaturacion = 0.95m;

        public MotorConstraintProgramming(ILogger<MotorConstraintProgramming> logger, CpSatOptions? options = null)
        {
            _logger = logger;
            _options = options ?? new CpSatOptions();
        }

        public Task<ResultadoFactibilidad> ResolverFactibilidadAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Grupo>? grupos = null,
            IEnumerable<Guid>? sesionesFijasIds = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            CancellationToken ct = default)
        {
            var s     = sesiones.ToList();
            var b     = bloques.ToList();
            var e     = espacios.ToList();
            var g     = grupos?.ToList() ?? new List<Grupo>();
            var fijas = sesionesFijasIds != null
                ? new HashSet<Guid>(sesionesFijasIds)
                : new HashSet<Guid>();
            var ventanas = ventanaPorAsignatura ?? new Dictionary<Guid, (TimeOnly?, TimeOnly?)>();
            return Task.Run(() => ResolverSincrono(s, b, e, g, fijas, ventanas, ct, permitirSweep: true), ct);
        }

        /// <summary>
        /// Modalidad derivada para una semana concreta. Dato fijo, no lo decide el solver.
        /// Una sesión marcada como Virtual (asignatura totalmente en línea) es virtual en ambas
        /// semanas; solo las sesiones presenciales alternan según TipoA/TipoB (regla 9).
        /// </summary>
        private static Modalidad ModalidadDe(Sesion sesion, SemanaAcademica semana)
            => ModalidadSemanal.Derivar(sesion, semana); // fuente única de la regla 9 (Domain)

        private static IReadOnlyList<AsignacionSemanal> SinAsignaciones => Array.Empty<AsignacionSemanal>();

        private ResultadoFactibilidad ResolverSincrono(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Espacio> espacios,
            List<Grupo> grupos,
            HashSet<Guid> sesionesFijasIds,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)> ventanaPorAsignatura,
            CancellationToken ct,
            bool permitirSweep,
            bool omitirRestriccionAulas = false)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques para procesar en la Fase 2.");
                return new ResultadoFactibilidad(false, SinAsignaciones, "No hay datos suficientes.", MotivoInfactibilidad.Datos);
            }

            // G2/G7 auditoría: los mensajes de infactibilidad interpolaban GUIDs (GrupoId,
            // AsignaturaId, SesionId) directamente — el frontend los pinta tal cual en el panel
            // de logs. Degrada a "grupo sin nombre" si el grupo no vino en la lista, nunca vuelve
            // a exponer el Id crudo.
            var nombrePorGrupo = grupos.ToDictionary(gr => gr.Id, gr => gr.Nombre);
            string NombreGrupo(Guid? grupoId) =>
                grupoId.HasValue && nombrePorGrupo.TryGetValue(grupoId.Value, out var n) ? n : "grupo sin nombre";

            // ── HC-SEP (pre-check estructural): un cluster de ≥4 sesiones semanales del mismo
            // (grupo, asignatura, tipo de sesión) es infactible SIEMPRE, sin importar la grilla —
            // con separación mínima de 2 días en un rango de 6 (Lunes..Sábado), el mayor conjunto
            // pairwise-separado posible es 3 (p. ej. lunes/miércoles/viernes; añadir un 4º día
            // rompe la separación con alguno de los tres). Antes esto caía en INFEASIBLE genérico,
            // que el catch-all final etiquetaba como MotivoInfactibilidad.Espacio — disparando en
            // falso el bucle de cesión de laboratorios (M5 del análisis).
            foreach (var cluster in sesiones
                         .Where(s => !sesionesFijasIds.Contains(s.Id) && s.GrupoId.HasValue)
                         .GroupBy(s => (Grupo: s.GrupoId!.Value, Asignatura: s.AsignaturaId, Tipo: CalculadorEspaciosSesion.TipoSesionDe(s)))
                         .Where(g => g.Count() >= 4))
            {
                var msg = $"No es posible programar esta asignatura: el grupo '{NombreGrupo(cluster.Key.Grupo)}' tiene " +
                          $"{cluster.Count()} sesiones semanales de tipo {cluster.Key.Tipo}, pero la separación " +
                          "mínima de 2 días entre sesiones del mismo tipo sólo admite 3 por semana sin solaparse " +
                          "(p. ej. lunes/miércoles/viernes). Reduzca las sesiones semanales de este tipo o repártalas " +
                          "en más de un grupo.";
                _logger.LogError(msg);
                return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Otro);
            }

            // ── Capacidad de espacios vs demanda presencial POR SEMANA Y POR CLASE ──────────
            // M1 (auditoría): antes sumaba TODOS los espacios en una sola bolsa — un run con 20
            // salones y 0 laboratorios pasaba este chequeo aunque toda sesión de laboratorio
            // estuviera condenada a fallar más abajo, y el mensaje de error hablaba de "espacios"
            // en general en vez de nombrar el tipo que realmente falta. Partición barata (no
            // sustituye el filtrado exacto de CalculadorEspaciosSesion.Candidatos, que sí respeta
            // el espacio fijo/tipo del requisito de grupo): Laboratorio vs el resto — las dos clases
            // que gobierna la regla por defecto de TipoSesion. Virtuales no ocupan espacio.
            int espaciosLab   = espacios.Count(e => e.Tipo == TipoEspacio.Laboratorio);
            int espaciosNoLab = espacios.Count - espaciosLab;
            foreach (var semana in Semanas)
            {
                // Fuente única de la ocupación de aula: una sesión que no alterna cuenta en las DOS
                // semanas (no libera nada), una TipoA solo en A y una TipoB solo en B.
                var presencialesSemana = sesiones
                    .Where(s => ModalidadSemanal.SemanasQueOcupanEspacio(s).Contains(semana))
                    .ToList();
                foreach (var (esLab, nombreClase, capacidadEspacios) in new[]
                         {
                             (true,  "laboratorios", espaciosLab),
                             (false, "salones/auditorios", espaciosNoLab)
                         })
                {
                    decimal demanda = presencialesSemana
                        .Where(s => (CalculadorEspaciosSesion.TipoSesionDe(s) == TipoSesion.Laboratorio) == esLab)
                        .Sum(s => s.DuracionHoras);
                    if (demanda == 0) continue;

                    int capacidadHoras = capacidadEspacios * bloques.Count;

                    _logger.LogInformation(
                        "Fase 2 (CP-SAT) Semana {W} [{Clase}]: demanda presencial={Dem}h vs capacidad={Cap}h ({E} espacios × {B} bloques).",
                        semana, nombreClase, demanda, capacidadHoras, capacidadEspacios, bloques.Count);

                    if (capacidadHoras > 0 && demanda / capacidadHoras >= UmbralSaturacion)
                    {
                        _logger.LogWarning(
                            "Fase 2 (CP-SAT) Semana {W} [{Clase}]: demanda al {Pct:P0} de la capacidad — el modelo es " +
                            "factible en teoría pero puede saturarse y agotar el timeout. Considere más {Clase} o " +
                            "revisar la distribución de alternancia.",
                            semana, nombreClase, demanda / capacidadHoras, nombreClase);
                    }

                    if (demanda > capacidadHoras)
                    {
                        // Camino rápido (sin resolver) que devuelve Espacio y alimenta el bucle de
                        // cesión de GenerarHorarioService: es la vía por la que se activa la Semana B.
                        var msg = $"No caben las {nombreClase} en la semana {semana}: se piden {demanda}h y solo hay " +
                                  $"{capacidadHoras}h disponibles ({capacidadEspacios} espacio(s) de ese tipo × " +
                                  $"{bloques.Count} bloques). Añada más {nombreClase}, o marque más asignaturas como " +
                                  "candidatas a alternancia para que puedan emparejarse y compartir aula en semanas alternas.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Espacio);
                    }
                }
            }

            var model = new CpModel();

            // ── Índices y helpers ───────────────────────────────────────────────────────
            var bloqueIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;

            var espacioIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < espacios.Count; i++) espacioIndex[espacios[i].Id] = i;

            var rangosPorDia = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // HC-G01: índice de disponibilidad por GrupoId.
            // Si el grupo declara Disponibilidad (Matutino/Vespertino), los starts de sus sesiones
            // quedan confinados a los bloques que caen dentro de esa franja (hard constraint).
            // Criterio de franja centralizado en CalculadorDominioSesion (misma fuente que el GA
            // y el validador post-generación).
            var bloquesPermitidosPorGrupo = new Dictionary<Guid, HashSet<int>>();
            foreach (var grupo in grupos)
            {
                if (grupo.Id == Guid.Empty) continue;
                var permitidos = CalculadorDominioSesion.BloquesPermitidos(bloques, grupo.ObtenerDisponibilidadSemanal());
                if (permitidos is not null)
                    bloquesPermitidosPorGrupo[grupo.Id] = permitidos;
            }

            // ── HC-G01 agregada: carga propia del grupo vs. su ventana declarada ──────
            // HC-C01 (NoOverlap por grupo) implica que si la suma de bloques que las sesiones
            // no fijas de un grupo necesitan supera los bloques que su disponibilidad
            // declarada permite, el modelo es infactible con certeza — sin necesidad de
            // resolver CP-SAT para probarlo, y sin dejar el mensaje genérico de siempre.
            var gruposSobrecargados = sesiones
                .Where(s => !sesionesFijasIds.Contains(s.Id) && s.GrupoId.HasValue
                            && bloquesPermitidosPorGrupo.ContainsKey(s.GrupoId.Value))
                .GroupBy(s => s.GrupoId!.Value)
                .Select(g => new
                {
                    GrupoId = g.Key,
                    Requeridos = g.Sum(s => Math.Max(1, (int)Math.Ceiling(s.DuracionHoras))),
                    Permitidos = bloquesPermitidosPorGrupo[g.Key].Count
                })
                .Where(x => x.Requeridos > x.Permitidos)
                .ToList();

            if (gruposSobrecargados.Count > 0)
            {
                var detalle = string.Join("; ", gruposSobrecargados.Select(x =>
                    $"'{NombreGrupo(x.GrupoId)}' necesita {x.Requeridos} bloque(s) y su disponibilidad solo permite {x.Permitidos}"));
                var msg = $"Disponibilidad insuficiente: {detalle}. Amplíe la disponibilidad declarada de ese/esos grupo(s) " +
                          "o reduzca/redistribuya sus sesiones semanales.";
                _logger.LogError(msg);
                return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.FranjaGrupo);
            }

            // HC-CAP: estudiantes por grupo. Un espacio solo es candidato si su aforo alcanza
            // para los estudiantes del grupo de la sesión (Espacio.Capacidad >= EstudiantesInscritos).
            var estudiantesPorGrupo = grupos
                .Where(gr => gr.Id != Guid.Empty)
                .GroupBy(gr => gr.Id)
                .ToDictionary(gr => gr.Key, gr => gr.First().EstudiantesInscritos);

            // HC-S03/HC-S05: requisitos de espacio por grupo (A2), indexados por GrupoId.
            var requisitosPorGrupo = grupos
                .Where(gr => gr.Id != Guid.Empty)
                .GroupBy(gr => gr.Id)
                .ToDictionary(gr => gr.Key, gr => gr.First().RequisitosEspacio);

            // ── Variables: UN intervalo de longitud DuracionHoras por sesión ────────────
            // Regla 9 / ALT-05: la franja es la misma en todas las semanas, así que no hay nada
            // que enlazar entre A y B — simplemente no existen dos variables que puedan divergir.
            var startVars    = new Dictionary<Guid, IntVar>();
            var endVars      = new Dictionary<Guid, IntVar>();
            var intervalVars = new Dictionary<Guid, IntervalVar>();
            var spaceVars    = new Dictionary<Guid, IntVar>(); // solo sesiones que ocupan aula
            var duraciones   = new Dictionary<Guid, int>();

            foreach (var sesion in sesiones)
            {
                int duracion = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));
                duraciones[sesion.Id] = duracion;

                var startVar = model.NewIntVar(0, bloques.Count - duracion, $"start_{sesion.Id}");
                var endVar   = model.NewIntVar(duracion, bloques.Count, $"end_{sesion.Id}");
                startVars[sesion.Id] = startVar;
                endVars[sesion.Id]   = endVar;
                intervalVars[sesion.Id] = model.NewIntervalVar(startVar, duracion, endVar, $"int_{sesion.Id}");

                if (espacios.Any() && ModalidadSemanal.SemanasQueOcupanEspacio(sesion).Count > 0)
                    spaceVars[sesion.Id] = model.NewIntVar(0, espacios.Count - 1, $"space_{sesion.Id}");
            }

            // ── HC-ALT: alternancia atómica por espacio (A4/VERIFICA) — cada pareja
            // (mismo Sesion.ParejaAlternanciaId) comparte el MISMO bloque y el MISMO espacio físico.
            // Con una sola variable por sesión, "comparten aula en semanas distintas" se declara
            // directamente: la separación entre semanas ya la hace SemanasQueOcupanEspacio en el
            // NoOverlap de aula, así que aquí solo hace falta la igualdad.
            foreach (var pareja in sesiones
                         .Where(s => s.ParejaAlternanciaId.HasValue)
                         .GroupBy(s => s.ParejaAlternanciaId!.Value)
                         .Where(g => g.Count() == 2))
            {
                var miembros = pareja.ToList();
                var s1 = miembros[0]; var s2 = miembros[1];
                model.Add(startVars[s1.Id] == startVars[s2.Id]);

                if (spaceVars.TryGetValue(s1.Id, out var sv1) && spaceVars.TryGetValue(s2.Id, out var sv2))
                {
                    model.Add(sv1 == sv2);
                }
                else
                {
                    // Pareja con un miembro que no ocupa aula (virtual puro): dato corrupto. Ceder
                    // más sesiones no lo arregla, así que se reporta como Otro y no como Espacio
                    // — si no, el bucle de cesión giraría sin avanzar.
                    var msg = $"Pareja de alternancia inválida entre la sesión de '{NombreGrupo(s1.GrupoId)}' y la de " +
                              $"'{NombreGrupo(s2.GrupoId)}': uno de los dos miembros no ocupa aula, así que no hay nada " +
                              "que alternar. Regenere el horario.";
                    _logger.LogError(msg);
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Otro);
                }
            }

            // ── Warm-start y fijación de sesiones base ───────────────────────────────
            // Los dos miembros de una pareja comparten start por HC-ALT, pero la coloración de
            // Fase 1 puede haberlos separado. Hintear cada uno a su propio bloque daría una pista
            // contradictoria que el solver descarta entera: se unifica al bloque del primer miembro.
            var hintDePareja = sesiones
                .Where(s => s.ParejaAlternanciaId.HasValue && bloqueIndex.ContainsKey(s.BloqueTiempoId))
                .GroupBy(s => s.ParejaAlternanciaId!.Value)
                .ToDictionary(g => g.Key, g => bloqueIndex[g.First().BloqueTiempoId]);

            foreach (var sesion in sesiones)
            {
                if (sesion.Estado != EstadoSesion.Asignada) continue;
                if (sesion.BloqueTiempoId == Guid.Empty) continue;
                if (!bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx)) continue;

                if (sesion.ParejaAlternanciaId is Guid parejaId && hintDePareja.TryGetValue(parejaId, out var idxPareja))
                    idx = idxPareja;

                int dur = duraciones[sesion.Id];
                if (!BloquesPlanner.CabeEnDia(idx, dur, rangosPorDia, diaPorIdx)) continue;

                if (sesionesFijasIds.Contains(sesion.Id))
                {
                    // Sesión del horario base: igualdad estricta — CP-SAT no puede moverla.
                    model.Add(startVars[sesion.Id] == idx);
                }
                else
                {
                    // Fase 1 warm-start: pista, el solver puede sobreescribirla.
                    model.AddHint(startVars[sesion.Id], idx);

                    // Si Fase 1 ya trae espacio asignado, hintear también spaceVars:
                    // si esa asignación es factible, CP-SAT la valida casi al instante.
                    if (sesion.EspacioId is Guid espacioHint &&
                        espacioIndex.TryGetValue(espacioHint, out var espIdx) &&
                        spaceVars.TryGetValue(sesion.Id, out var spaceVar))
                        model.AddHint(spaceVar, espIdx);
                }
            }

            // ── HC-G01 + "No cruzar día": dominio de start por sesión y semana ────────────
            // HC-G01 (presencial-first): si el grupo del grupo declara Disponibilidad, los starts
            // quedan confinados a los bloques de esa franja (hard constraint).
            // La disponibilidad docente YA NO restringe (CR-08): bloquesDisponibles para el
            // filtro de docente siempre es null. Las sesiones fijas ya tienen igualdad — se omiten.
            foreach (var sesion in sesiones)
            {
                if (sesionesFijasIds.Contains(sesion.Id)) continue;

                int dur = duraciones[sesion.Id];

                // Obtener restricción de disponibilidad del grupo (HC-G01)
                HashSet<int>? permitidosPorGrupo = null;
                if (sesion.GrupoId.HasValue &&
                    bloquesPermitidosPorGrupo.TryGetValue(sesion.GrupoId.Value, out var perm))
                    permitidosPorGrupo = perm;

                // Dominio base (fuente única CalculadorDominioSesion): cabe-en-día ∩ HC-G01.
                var startsGrupo = CalculadorDominioSesion.StartsPermitidos(
                    dur, bloques, rangosPorDia, diaPorIdx, permitidosPorGrupo);

                if (startsGrupo.Length == 0)
                {
                    var msg = permitidosPorGrupo is not null
                        ? $"Sin bloques disponibles: la sesión de {dur}h del grupo '{NombreGrupo(sesion.GrupoId)}' no cabe dentro de su disponibilidad horaria declarada."
                        : $"No hay bloques válidos para una sesión de {dur}h (no cabe sin cruzar día en la grilla canónica).";
                    _logger.LogError(msg);
                    var motivo = permitidosPorGrupo is not null ? MotivoInfactibilidad.FranjaGrupo : MotivoInfactibilidad.Otro;
                    return new ResultadoFactibilidad(false, SinAsignaciones, msg, motivo);
                }

                // HC-VH: ventana horaria de la asignatura (hard). El intervalo completo
                // [inicio, inicio+dur] debe caer dentro de [min, max]. dur ya viene con ceil:
                // redondeo conservador (sobre-reserva, nunca sub-reserva). Como el dominio base
                // garantiza que la sesión no cruza día, inicio+dur nunca se desborda al día siguiente.
                var startsFiltrados = startsGrupo;
                if (ventanaPorAsignatura.TryGetValue(sesion.AsignaturaId, out var ventana) &&
                    (ventana.min.HasValue || ventana.max.HasValue))
                {
                    startsFiltrados = startsGrupo.Where(s => CalculadorDominioSesion.CumpleVentana(
                        bloques[s].HoraInicio, dur, ventana.min, ventana.max)).ToArray();

                    if (startsFiltrados.Length == 0)
                    {
                        var msg = $"Fuera de la ventana horaria: la sesión de {dur}h del grupo '{NombreGrupo(sesion.GrupoId)}' no " +
                                  $"cabe dentro de la ventana horaria permitida [{ventana.min:HH\\:mm}–{ventana.max:HH\\:mm}] " +
                                  "en ningún día de la grilla. Amplíe la ventana o reduzca la duración.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.VentanaHoraria);
                    }
                }

                var dominio = CpDomain.FromValues(startsFiltrados.Select(v => (long)v).ToArray());
                model.AddLinearExpressionInDomain(startVars[sesion.Id], dominio);
            }

            // ── HC-SEP: separación mínima de días entre sesiones semanales del mismo
            // (grupo, asignatura, tipo de sesión) — petición 11 (A5, ReglasSesion). Canaliza el
            // día de cada start con AddElement y exige, por cada par, que el día difiera en ≥2
            // posiciones (lunes/martes no; lunes/miércoles sí). Es un eje TEMPORAL, así que no
            // depende de la semana: la sesión cae el mismo día todas las semanas. Sesiones fijas
            // quedan fuera: ya están fijadas por igualdad y no participan de este dominio.
            var diaConstPorIdx = bloques.Select(bl => model.NewConstant((int)bl.Dia)).ToArray();
            var gruposSeparacion = sesiones
                .Where(s => !sesionesFijasIds.Contains(s.Id) && s.GrupoId.HasValue)
                .GroupBy(s => (s.GrupoId!.Value, s.AsignaturaId, Tipo: CalculadorEspaciosSesion.TipoSesionDe(s)))
                .Where(g => g.Count() >= 2);
            foreach (var grupo in gruposSeparacion)
            {
                var lista = grupo.ToList();
                var diaVars = lista.Select(s =>
                {
                    var diaVar = model.NewIntVar(0, 5, $"dia_{s.Id}");
                    model.AddElement(startVars[s.Id], diaConstPorIdx, diaVar);
                    return diaVar;
                }).ToList();

                for (int i = 0; i < diaVars.Count; i++)
                    for (int j = i + 1; j < diaVars.Count; j++)
                    {
                        var b = model.NewBoolVar($"sep_{lista[i].Id}_{lista[j].Id}");
                        model.Add(diaVars[i] - diaVars[j] >= 2).OnlyEnforceIf(b);
                        model.Add(diaVars[j] - diaVars[i] >= 2).OnlyEnforceIf(b.Not());
                    }
            }

            // ── HC-C01: conflicto de cohorte — NoOverlap por grupo ────────────────────
            // CR-08 (presencial-first): el grupo de estudiantes es el eje de no-solapamiento (el
            // docente sale del pipeline y se asigna después de generar). Incluye presenciales y
            // virtuales: ambos consumen el tiempo del grupo.
            //
            // INDEPENDIENTE DE LA SEMANA: una sesión que alterna se sigue dictando la semana
            // contraria, virtualmente, así que ocupa el tiempo de su cohorte en las dos. Dos
            // sesiones del mismo grupo a la misma hora chocan alterne quien alterne — y de ahí sale
            // que una pareja de alternancia DEBE ser de dos grupos distintos (HC-ALT las fuerza al
            // mismo bloque). Emparejar dentro del mismo grupo produce infactibilidad con motivo
            // Otro, no Espacio, que es justo lo que el bucle de cesión no sabe interpretar.
            var sesionesPorGrupo = sesiones.GroupBy(s => s.GrupoId).Where(g => g.Key.HasValue).ToList();
            foreach (var grupo in sesionesPorGrupo)
            {
                var intervals = grupo.Select(s => intervalVars[s.Id]).ToArray();
                if (intervals.Length > 1)
                    model.AddNoOverlap(intervals);
            }

            // ── HC-S01 + HC-S03 + HC-S04: espacio por (espacio, semana), solo presencial ─
            // omitirRestriccionAulas: relajación de diagnóstico — ver ClasificarInfactibilidadEspacio.
            if (!omitirRestriccionAulas && spaceVars.Any() && espacios.Any())
            {
                // HC-S03 + HC-S05: candidatos por sesión.
                // HC-S05: si la sesión trae un EspacioId específico y ese espacio existe en la
                // lista, solo ese espacio es candidato (respeta la asignación del curriculum).
                // HC-S03: si no hay espacio fijo pero requiere laboratorio, se filtran por tipo.
                var candidatosPorSesion = new Dictionary<Guid, List<int>>();
                foreach (var sesion in sesiones)
                {
                    if (!spaceVars.ContainsKey(sesion.Id)) continue;

                    // M8: EspacioId fijo de la sesión que no está entre los espacios de esta corrida
                    // (p. ej. borrado del catálogo) hace que CalculadorEspaciosSesion.Candidatos caiga
                    // al filtro genérico por tipo/requisito de grupo en vez de fallar — antes, en
                    // silencio. Advertirlo aquí en vez de en el Domain (SOEA.Domain no puede depender
                    // de ILogger — regla 2 de arquitectura).
                    if (sesion.EspacioId is Guid espacioSesionFijo && !espacioIndex.ContainsKey(espacioSesionFijo))
                        _logger.LogWarning(
                            "Sesión {SesionId}: el espacio fijo {EspacioId} no está entre los espacios de esta " +
                            "corrida; se usará el filtro por tipo/requisito de grupo en su lugar.",
                            sesion.Id, espacioSesionFijo);

                    // HC-S05 (espacio fijo, de la sesión o del requisito del grupo) ∩ HC-S03
                    // (tipo de espacio según TipoSesion — A2/A3, fuente única).
                    RequisitoEspacio? requisito = sesion.GrupoId.HasValue &&
                        requisitosPorGrupo.TryGetValue(sesion.GrupoId.Value, out var reqs)
                        ? reqs.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(sesion))
                        : null;
                    var lista = CalculadorEspaciosSesion.Candidatos(sesion, espacios, requisito).ToList();

                    if (lista.Count == 0)
                    {
                        var msg = $"La sesión de {CalculadorEspaciosSesion.TipoSesionDe(sesion)} del grupo '{NombreGrupo(sesion.GrupoId)}' " +
                                  "no tiene ningún espacio candidato del tipo requerido entre los espacios configurados.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Espacio);
                    }

                    // HC-CAP: descartar espacios con aforo insuficiente para el grupo de la sesión.
                    int estudiantes = sesion.GrupoId.HasValue &&
                        estudiantesPorGrupo.TryGetValue(sesion.GrupoId.Value, out var nEst) ? nEst : 0;
                    if (estudiantes > 0)
                    {
                        var conAforo = lista.Where(e => espacios[e].Capacidad >= estudiantes).ToList();
                        if (conAforo.Count == 0)
                        {
                            int aforoMax = lista.Max(e => espacios[e].Capacidad);
                            var msg = $"Capacidad insuficiente: la sesión del grupo '{NombreGrupo(sesion.GrupoId)}' necesita un espacio para {estudiantes} " +
                                      $"estudiantes, pero el aforo máximo disponible entre sus candidatos es {aforoMax}. " +
                                      "Añada un espacio con mayor capacidad o reduzca el grupo.";
                            _logger.LogError(msg);
                            return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Espacio);
                        }
                        lista = conAforo;
                    }

                    candidatosPorSesion[sesion.Id] = lista;
                }

                // Intervalos opcionales agrupados por (espacio, semana). La sesión tiene UN solo
                // intervalo; lo que varía es en cuántas listas semanales entra:
                //   - no alterna ⇒ entra en A y en B (misma variable en las dos listas: ocupa el
                //     aula todas las semanas, así que NO libera capacidad para nadie);
                //   - TipoA ⇒ solo en A · TipoB ⇒ solo en B, y por eso una pareja puede compartir
                //     aula y bloque sin que las dos listas la vean nunca a la vez.
                // Fuente única del reparto: ModalidadSemanal.SemanasQueOcupanEspacio.
                var optIntervalsPorEspacioSemana = new Dictionary<(int espacio, SemanaAcademica semana), List<IntervalVar>>();
                for (int e = 0; e < espacios.Count; e++)
                    foreach (var semana in Semanas)
                        optIntervalsPorEspacioSemana[(e, semana)] = new List<IntervalVar>();

                var sesionPorIdModelo = sesiones.ToDictionary(x => x.Id);
                foreach (var sesionId in spaceVars.Keys)
                {
                    if (!candidatosPorSesion.TryGetValue(sesionId, out var candidatos)) continue;
                    var semanasOcupadas = ModalidadSemanal.SemanasQueOcupanEspacio(sesionPorIdModelo[sesionId]);

                    var literales = new List<ILiteral>();
                    foreach (var e in candidatos)
                    {
                        var lit = model.NewBoolVar($"sel_{sesionId}_{e}");
                        literales.Add(lit);

                        model.Add(spaceVars[sesionId] == e).OnlyEnforceIf(lit);

                        var optInt = model.NewOptionalIntervalVar(
                            startVars[sesionId],
                            duraciones[sesionId],
                            endVars[sesionId],
                            lit,
                            $"optInt_{sesionId}_{e}");
                        foreach (var semana in semanasOcupadas)
                            optIntervalsPorEspacioSemana[(e, semana)].Add(optInt);
                    }

                    model.AddExactlyOne(literales);
                }

                // NoOverlap por cada (espacio físico, semana).
                foreach (var par in optIntervalsPorEspacioSemana)
                {
                    if (par.Value.Count > 1)
                        model.AddNoOverlap(par.Value);
                }
            }

            // ── RESOLVER ────────────────────────────────────────────────────────────────
            // El volcado a disco solo ocurre si se habilita explícitamente (P0.2 auditoría).
            if (_options.ExportarModelo)
            {
                try
                {
                    System.IO.File.WriteAllText("cp_model_debug.txt", model.Model.ToString());
                    _logger.LogInformation("Modelo CP-SAT exportado a cp_model_debug.txt para inspección.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo exportar el modelo CP-SAT.");
                }
            }

            var solver = new CpSolver();
            solver.StringParameters =
                $"max_time_in_seconds:{_options.TimeoutSegundos}" +
                (_options.NumWorkers > 0 ? $",num_search_workers:{_options.NumWorkers}" : "");

            _logger.LogInformation("Resolviendo modelo CP-SAT (timeout: {T}s)...", _options.TimeoutSegundos);
            using var reg = ct.Register(() => solver.StopSearch());
            var status = solver.Solve(model);
            ct.ThrowIfCancellationRequested();
            _logger.LogInformation("CP-SAT terminó con status: {Status}", status);

            if (status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal)
            {
                // UNA fila por sesión, en su semana canónica: la semana en la que es presencial
                // (B solo para TipoB). Para lo que no alterna, "A" significa "todas las semanas".
                // La contraparte virtual de una sesión que alterna no se persiste — no reserva
                // aula, así que no aporta nada al modelo; se deriva al construir el DTO.
                var asignaciones = new List<AsignacionSemanal>(sesiones.Count);
                foreach (var sesion in sesiones)
                {
                    var startIdx = (int)solver.Value(startVars[sesion.Id]);
                    var bloqueAsignado = bloques[startIdx];

                    var modalidad = ModalidadSemanal.ModalidadCanonica(sesion);
                    Guid? espacioAsignado = null;
                    if (modalidad == Modalidad.Presencial && spaceVars.TryGetValue(sesion.Id, out var spaceVar))
                    {
                        var espacioIdx = (int)solver.Value(spaceVar);
                        if (espacioIdx >= 0 && espacioIdx < espacios.Count)
                            espacioAsignado = espacios[espacioIdx].Id;
                    }

                    asignaciones.Add(new AsignacionSemanal(
                        Guid.NewGuid(),
                        sesion.Id,
                        ModalidadSemanal.SemanaCanonica(sesion),
                        bloqueAsignado.Id,
                        espacioAsignado,
                        modalidad));
                }

                _logger.LogInformation(
                    "Fase 2 completada exitosamente. {N} asignaciones semanales (1 por sesión) factibles.",
                    asignaciones.Count);
                return new ResultadoFactibilidad(true, asignaciones.AsReadOnly(), "");
            }

            _logger.LogWarning("CP-SAT no encontró solución factible. Status: {Status}", status);

            // M3 (auditoría): antes CUALQUIER fallo del solver que llegara hasta aquí (timeout,
            // HC-SEP/HC-ALT contradictorios, modelo mal formado) se reportaba como MotivoInfactibilidad
            // .Espacio — el mismo motivo que dispara el bucle de cesión de laboratorios en
            // GenerarHorarioService. Un timeout hacía que el sistema virtualizara labs por una causa
            // que no tenía nada que ver con el espacio físico. Unknown (timeout) y Infeasible/otro
            // (contradicción real de restricciones) ahora se diagnostican por separado.
            if (status == CpSolverStatus.Unknown)
            {
                return new ResultadoFactibilidad(false, SinAsignaciones,
                    $"El solver agotó el tiempo límite ({_options.TimeoutSegundos}s) sin determinar si existe una " +
                    "solución factible. Puede haber una solución que no se encontró a tiempo — aumente el timeout " +
                    "o reduzca el número de sesiones del run.",
                    MotivoInfactibilidad.Timeout);
            }

            var mensaje = "El modelo no tiene solución factible (status del solver: " + status + "). Ninguna combinación " +
                "de horario satisface todas las restricciones duras configuradas — revise la separación mínima de días " +
                "entre sesiones del mismo tipo, las parejas de alternancia, o la combinación de disponibilidad de grupo " +
                "y ventana horaria.";

            // ¿Son las aulas? El pre-check agregado solo ve el total semanal; un cuello de botella
            // por bloque concreto llega hasta aquí y, sin clasificar, se reportaría como Otro — y el
            // bucle de cesión de GenerarHorarioService nunca activaría la Semana B. Una relajación
            // sin restricciones de aula lo responde con un solo solve extra.
            bool esFaltaDeAulas = false;
            if (permitirSweep && !omitirRestriccionAulas && _options.ClasificarInfactibilidadEspacio
                && spaceVars.Count > 0 && espacios.Count > 0)
            {
                var sinAulas = ResolverSincrono(
                    sesiones, bloques, espacios, grupos, sesionesFijasIds, ventanaPorAsignatura, ct,
                    permitirSweep: false, omitirRestriccionAulas: true);

                if (sinAulas.EsFactible)
                {
                    esFaltaDeAulas = true;
                    mensaje = "No alcanzan las aulas: existe una distribución horaria válida, pero no hay forma de " +
                              "repartir los espacios sin que dos sesiones coincidan en la misma aula y bloque. " +
                              "Añada espacios, amplíe la disponibilidad de los grupos, o marque más asignaturas " +
                              "como candidatas a alternancia para que puedan compartir aula en semanas alternas.";
                    _logger.LogError(mensaje);
                }
            }

            // El barrido corre igual cuando la causa son las aulas: el motivo Espacio dice QUÉ hacer
            // (emparejar / añadir aulas) y el barrido dice A QUIÉN mirar. Son complementarios.
            // Causa real no explicada por ningún pre-check estructural: si está habilitado, el
            // barrido reintenta el solve una vez por grupo excluyéndolo para nombrar culpables.
            IReadOnlyList<Guid>? gruposResponsablesIds = null;
            if (permitirSweep)
            {
                var gruposResponsables = EjecutarSweepDiagnostico(
                    sesiones, bloques, espacios, grupos, sesionesFijasIds, ventanaPorAsignatura, ct);
                if (gruposResponsables is not null)
                {
                    gruposResponsablesIds = gruposResponsables.Select(g => g.Id).ToList();
                    mensaje += gruposResponsables.Count > 0
                        ? $" Diagnóstico adicional: al excluir el grupo '{string.Join("' o '", gruposResponsables.Select(g => g.Nombre))}' " +
                          "el modelo pasa a ser factible; revise conflictos entre esos grupos (espacio compartido, " +
                          "pareja de alternancia, u otra restricción cruzada)."
                        : " Diagnóstico adicional: ningún grupo individual es responsable — revise la capacidad global, " +
                          "la separación mínima de días entre sesiones, o las parejas de alternancia.";
                }
            }

            return new ResultadoFactibilidad(false, SinAsignaciones, mensaje,
                esFaltaDeAulas ? MotivoInfactibilidad.Espacio : MotivoInfactibilidad.Otro,
                gruposResponsablesIds);
        }

        /// <summary>
        /// Reintenta el solve una vez por cada grupo con sesiones propias, excluyéndolo, para
        /// identificar cuáles son responsables de una infactibilidad que ningún pre-check
        /// estructural explicó. Solo se invoca desde el catch-all final, con permitirSweep=false
        /// en las resoluciones recursivas (nunca dispara un segundo barrido). Devuelve null si el
        /// barrido no corrió (deshabilitado o demasiados grupos candidatos).
        /// </summary>
        private List<Grupo>? EjecutarSweepDiagnostico(
            List<Sesion> sesiones, List<BloqueTiempo> bloques, List<Espacio> espacios,
            List<Grupo> grupos, HashSet<Guid> sesionesFijasIds,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)> ventanaPorAsignatura,
            CancellationToken ct)
        {
            if (!_options.SweepGrupos) return null;

            var candidatos = grupos
                .Where(gr => sesiones.Any(s => s.GrupoId == gr.Id && !sesionesFijasIds.Contains(s.Id)))
                .ToList();
            if (candidatos.Count == 0) return null;

            if (candidatos.Count > _options.SweepGruposMaximo)
            {
                _logger.LogWarning(
                    "Barrido de diagnóstico omitido: {N} grupos candidatos superan el tope configurado ({Max}).",
                    candidatos.Count, _options.SweepGruposMaximo);
                return null;
            }

            var responsables = new List<Grupo>();
            try
            {
                foreach (var candidato in candidatos)
                {
                    var sesionesReducidas = sesiones.Where(s => s.GrupoId != candidato.Id).ToList();
                    var gruposReducidos = grupos.Where(gr => gr.Id != candidato.Id).ToList();
                    var resultadoReducido = ResolverSincrono(
                        sesionesReducidas, bloques, espacios, gruposReducidos, sesionesFijasIds,
                        ventanaPorAsignatura, ct, permitirSweep: false);
                    if (resultadoReducido.EsFactible)
                        responsables.Add(candidato);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "El barrido de diagnóstico por grupo falló; se omite el diagnóstico adicional.");
                return null;
            }
            return responsables;
        }
    }
}
