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
    /// Modela cada sesión como DOS intervalos de longitud fija = DuracionHoras, uno por
    /// <see cref="SemanaAcademica"/> (A / B), e impone NoOverlap por (grupo, semana) — HC-C01,
    /// CR-08: el docente sale del pipeline — y por (espacio, semana) — HC-S01. La modalidad por
    /// semana se DERIVA de la alternancia (dato fijo):
    /// TipoA → presencial en A / virtual en B; TipoB → virtual en A / presencial en B;
    /// SinAlternancia → presencial en ambas. Para TipoA/TipoB la franja virtual se enlaza a la
    /// presencial (regla 9: misma franja). La duración es inmutable (CLAUDE.md regla 6).
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
            bool permitirSweep)
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
                var msg = $"HC-SEP infactible: el grupo '{NombreGrupo(cluster.Key.Grupo)}' tiene {cluster.Count()} sesiones " +
                          $"semanales de tipo {cluster.Key.Tipo} para esa asignatura, pero la separación " +
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
                var presencialesSemana = sesiones.Where(s => ModalidadDe(s, semana) == Modalidad.Presencial).ToList();
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
                        var msg = $"Infactible (Semana {semana}): la demanda de {nombreClase} ({demanda}h) supera la " +
                                  $"capacidad disponible ({capacidadHoras}h = {capacidadEspacios} espacio(s) de ese tipo " +
                                  $"× {bloques.Count} bloques). Añada más {nombreClase} o ajuste la alternancia.";
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
                var msg = $"HC-G01 agregada infactible: {detalle}. Amplíe la disponibilidad declarada de ese/esos grupo(s) " +
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

            // ── Variables: intervalo de longitud DuracionHoras por (sesión, semana) ─────
            var startVars    = new Dictionary<(Guid, SemanaAcademica), IntVar>();
            var endVars      = new Dictionary<(Guid, SemanaAcademica), IntVar>();
            var intervalVars = new Dictionary<(Guid, SemanaAcademica), IntervalVar>();
            var spaceVars    = new Dictionary<(Guid, SemanaAcademica), IntVar>(); // solo pares presenciales
            var duraciones   = new Dictionary<Guid, int>();

            foreach (var sesion in sesiones)
            {
                int duracion = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));
                duraciones[sesion.Id] = duracion;

                foreach (var semana in Semanas)
                {
                    var key = (sesion.Id, semana);
                    var startVar = model.NewIntVar(0, bloques.Count - duracion, $"start_{sesion.Id}_{semana}");
                    var endVar   = model.NewIntVar(duracion, bloques.Count, $"end_{sesion.Id}_{semana}");
                    startVars[key] = startVar;
                    endVars[key]   = endVar;
                    intervalVars[key] = model.NewIntervalVar(startVar, duracion, endVar, $"int_{sesion.Id}_{semana}");

                    if (espacios.Any() && ModalidadDe(sesion, semana) == Modalidad.Presencial)
                        spaceVars[key] = model.NewIntVar(0, espacios.Count - 1, $"space_{sesion.Id}_{semana}");
                }
            }

            // ── Enlace regla 9: para alternancia, la franja virtual = franja presencial ─
            foreach (var sesion in sesiones)
            {
                if (sesion.Alternancia is TipoAlternancia.TipoA or TipoAlternancia.TipoB)
                    model.Add(startVars[(sesion.Id, SemanaAcademica.A)] == startVars[(sesion.Id, SemanaAcademica.B)]);
            }

            // ── HC-ALT: alternancia atómica por espacio (A4/VERIFICA) — cada pareja
            // (mismo Sesion.ParejaAlternanciaId) comparte el MISMO bloque y el MISMO espacio físico,
            // una presencial en semana A y la otra en B. "Mismo start" basta declararlo para la
            // Semana A: el enlace regla 9 de cada sesión (arriba) ya liga su propia A↔B.
            foreach (var pareja in sesiones
                         .Where(s => s.ParejaAlternanciaId.HasValue)
                         .GroupBy(s => s.ParejaAlternanciaId!.Value)
                         .Where(g => g.Count() == 2))
            {
                var miembros = pareja.ToList();
                var s1 = miembros[0]; var s2 = miembros[1];
                model.Add(startVars[(s1.Id, SemanaAcademica.A)] == startVars[(s2.Id, SemanaAcademica.A)]);

                // Cada miembro solo tiene spaceVar en su propia semana presencial (TipoA→A, TipoB→B);
                // igualarlas cruzando semana es justamente "comparten el mismo espacio, en semanas
                // distintas" — la atomicidad por espacio que pide VERIFICA.
                if (spaceVars.TryGetValue((s1.Id, SemanaAcademica.A), out var sv1A) &&
                    spaceVars.TryGetValue((s2.Id, SemanaAcademica.B), out var sv2B))
                    model.Add(sv1A == sv2B);
                else if (spaceVars.TryGetValue((s1.Id, SemanaAcademica.B), out var sv1B) &&
                         spaceVars.TryGetValue((s2.Id, SemanaAcademica.A), out var sv2A))
                    model.Add(sv1B == sv2A);
            }

            // ── Warm-start y fijación de sesiones base ───────────────────────────────
            foreach (var sesion in sesiones)
            {
                if (sesion.Estado != EstadoSesion.Asignada) continue;
                if (sesion.BloqueTiempoId == Guid.Empty) continue;
                if (!bloqueIndex.TryGetValue(sesion.BloqueTiempoId, out var idx)) continue;

                int dur = duraciones[sesion.Id];
                if (!BloquesPlanner.CabeEnDia(idx, dur, rangosPorDia, diaPorIdx)) continue;

                if (sesionesFijasIds.Contains(sesion.Id))
                {
                    // Sesión del horario base: igualdad estricta — CP-SAT no puede moverla.
                    foreach (var semana in Semanas)
                        model.Add(startVars[(sesion.Id, semana)] == idx);
                }
                else
                {
                    // Fase 1 warm-start: pista, el solver puede sobreescribirla.
                    foreach (var semana in Semanas)
                    {
                        model.AddHint(startVars[(sesion.Id, semana)], idx);

                        // Si Fase 1 ya trae espacio asignado, hintear también spaceVars:
                        // si esa asignación es factible, CP-SAT la valida casi al instante.
                        if (sesion.EspacioId is Guid espacioHint &&
                            espacioIndex.TryGetValue(espacioHint, out var espIdx) &&
                            spaceVars.TryGetValue((sesion.Id, semana), out var spaceVar))
                            model.AddHint(spaceVar, espIdx);
                    }
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
                        ? $"HC-G01: no hay bloques válidos para la sesión de {dur}h del grupo '{NombreGrupo(sesion.GrupoId)}' dentro de su disponibilidad declarada."
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
                        var msg = $"HC-VH infactible: la sesión de {dur}h del grupo '{NombreGrupo(sesion.GrupoId)}' no " +
                                  $"cabe dentro de su ventana horaria [{ventana.min:HH\\:mm}–{ventana.max:HH\\:mm}] " +
                                  "en ningún día de la grilla. Amplíe la ventana o reduzca la duración.";
                        _logger.LogError(msg);
                        return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.VentanaHoraria);
                    }
                }

                var dominio = CpDomain.FromValues(startsFiltrados.Select(v => (long)v).ToArray());
                foreach (var semana in Semanas)
                    model.AddLinearExpressionInDomain(startVars[(sesion.Id, semana)], dominio);
            }

            // ── HC-SEP: separación mínima de días entre sesiones semanales del mismo
            // (grupo, asignatura, tipo de sesión) — petición 11 (A5, ReglasSesion). Canaliza el
            // día de cada start con AddElement y exige, por cada par y por semana, que el día
            // difiera en ≥2 posiciones (lunes/martes no; lunes/miércoles sí). Sesiones fijas
            // quedan fuera: ya están fijadas por igualdad y no participan de este dominio.
            var diaConstPorIdx = bloques.Select(bl => model.NewConstant((int)bl.Dia)).ToArray();
            var gruposSeparacion = sesiones
                .Where(s => !sesionesFijasIds.Contains(s.Id) && s.GrupoId.HasValue)
                .GroupBy(s => (s.GrupoId!.Value, s.AsignaturaId, Tipo: CalculadorEspaciosSesion.TipoSesionDe(s)))
                .Where(g => g.Count() >= 2);
            foreach (var grupo in gruposSeparacion)
            {
                var lista = grupo.ToList();
                foreach (var semana in Semanas)
                {
                    var diaVars = lista.Select(s =>
                    {
                        var diaVar = model.NewIntVar(0, 5, $"dia_{s.Id}_{semana}");
                        model.AddElement(startVars[(s.Id, semana)], diaConstPorIdx, diaVar);
                        return diaVar;
                    }).ToList();

                    for (int i = 0; i < diaVars.Count; i++)
                        for (int j = i + 1; j < diaVars.Count; j++)
                        {
                            var b = model.NewBoolVar($"sep_{lista[i].Id}_{lista[j].Id}_{semana}");
                            model.Add(diaVars[i] - diaVars[j] >= 2).OnlyEnforceIf(b);
                            model.Add(diaVars[j] - diaVars[i] >= 2).OnlyEnforceIf(b.Not());
                        }
                }
            }

            // ── HC-C01: conflicto de cohorte — NoOverlap por (grupo, semana) ──────────
            // CR-08 (presencial-first): el grupo de estudiantes es el eje de no-solapamiento (el
            // docente sale del pipeline y se asigna después de generar). Incluye presenciales y
            // virtuales: ambos consumen el tiempo del grupo. Con cohorte única por run ⇒ un
            // NoOverlap global por semana (todas las sesiones se serializan). No hay equivalente
            // de "máx. horas" para el grupo, así que HC-I03 (carga docente) desaparece.
            var sesionesPorGrupo = sesiones.GroupBy(s => s.GrupoId).Where(g => g.Key.HasValue).ToList();
            foreach (var grupo in sesionesPorGrupo)
            {
                foreach (var semana in Semanas)
                {
                    var intervals = grupo.Select(s => intervalVars[(s.Id, semana)]).ToArray();
                    if (intervals.Length > 1)
                        model.AddNoOverlap(intervals);
                }
            }

            // ── HC-S01 + HC-S03 + HC-S04: espacio por (espacio, semana), solo presencial ─
            if (spaceVars.Any() && espacios.Any())
            {
                // HC-S03 + HC-S05: candidatos por sesión.
                // HC-S05: si la sesión trae un EspacioId específico y ese espacio existe en la
                // lista, solo ese espacio es candidato (respeta la asignación del curriculum).
                // HC-S03: si no hay espacio fijo pero requiere laboratorio, se filtran por tipo.
                var candidatosPorSesion = new Dictionary<Guid, List<int>>();
                foreach (var sesion in sesiones)
                {
                    bool tienePresencial = Semanas.Any(w => spaceVars.ContainsKey((sesion.Id, w)));
                    if (!tienePresencial) continue;

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
                            var msg = $"HC-CAP infactible: la sesión del grupo '{NombreGrupo(sesion.GrupoId)}' necesita un espacio para {estudiantes} " +
                                      $"estudiantes, pero el aforo máximo disponible entre sus candidatos es {aforoMax}. " +
                                      "Añada un espacio con mayor capacidad o reduzca el grupo.";
                            _logger.LogError(msg);
                            return new ResultadoFactibilidad(false, SinAsignaciones, msg, MotivoInfactibilidad.Espacio);
                        }
                        lista = conAforo;
                    }

                    candidatosPorSesion[sesion.Id] = lista;
                }

                // Intervalos opcionales agrupados por (espacio, semana).
                var optIntervalsPorEspacioSemana = new Dictionary<(int espacio, SemanaAcademica semana), List<IntervalVar>>();
                for (int e = 0; e < espacios.Count; e++)
                    foreach (var semana in Semanas)
                        optIntervalsPorEspacioSemana[(e, semana)] = new List<IntervalVar>();

                foreach (var key in spaceVars.Keys)
                {
                    var (sesionId, semana) = key;
                    if (!candidatosPorSesion.TryGetValue(sesionId, out var candidatos)) continue;

                    var literales = new List<ILiteral>();
                    foreach (var e in candidatos)
                    {
                        var lit = model.NewBoolVar($"sel_{sesionId}_{semana}_{e}");
                        literales.Add(lit);

                        model.Add(spaceVars[key] == e).OnlyEnforceIf(lit);

                        var optInt = model.NewOptionalIntervalVar(
                            startVars[key],
                            duraciones[sesionId],
                            endVars[key],
                            lit,
                            $"optInt_{sesionId}_{semana}_{e}");
                        optIntervalsPorEspacioSemana[(e, semana)].Add(optInt);
                    }

                    model.AddExactlyOne(literales);
                }

                // NoOverlap por cada (espacio físico, semana): un slot puede reusarse en semanas distintas.
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
                var asignaciones = new List<AsignacionSemanal>(sesiones.Count * 2);
                foreach (var sesion in sesiones)
                {
                    foreach (var semana in Semanas)
                    {
                        var key = (sesion.Id, semana);
                        var startIdx = (int)solver.Value(startVars[key]);
                        var bloqueAsignado = bloques[startIdx];

                        var modalidad = ModalidadDe(sesion, semana);
                        Guid? espacioAsignado = null;
                        if (spaceVars.TryGetValue(key, out var spaceVar))
                        {
                            var espacioIdx = (int)solver.Value(spaceVar);
                            if (espacioIdx >= 0 && espacioIdx < espacios.Count)
                                espacioAsignado = espacios[espacioIdx].Id;
                        }

                        asignaciones.Add(new AsignacionSemanal(
                            Guid.NewGuid(),
                            sesion.Id,
                            semana,
                            bloqueAsignado.Id,
                            espacioAsignado,
                            modalidad));
                    }
                }

                _logger.LogInformation(
                    "Fase 2 completada exitosamente. {N} asignaciones semanales (2 por sesión) factibles.",
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
                "de horario satisface todas las restricciones duras configuradas — revise separación mínima de días " +
                "(HC-SEP), parejas de alternancia (HC-ALT), o la combinación de disponibilidad de grupo y ventana horaria.";

            // Causa real no explicada por ningún pre-check estructural: si está habilitado, el
            // barrido reintenta el solve una vez por grupo excluyéndolo para nombrar culpables.
            if (permitirSweep)
            {
                var gruposResponsables = EjecutarSweepDiagnostico(
                    sesiones, bloques, espacios, grupos, sesionesFijasIds, ventanaPorAsignatura, ct);
                if (gruposResponsables is not null)
                {
                    mensaje += gruposResponsables.Count > 0
                        ? $" Diagnóstico adicional: al excluir el grupo '{string.Join("' o '", gruposResponsables)}' " +
                          "el modelo pasa a ser factible; revise conflictos entre esos grupos (espacio compartido, " +
                          "pareja de alternancia, u otra restricción cruzada)."
                        : " Diagnóstico adicional: ningún grupo individual es responsable — revise capacidad global " +
                          "o HC-SEP/HC-ALT.";
                }
            }

            return new ResultadoFactibilidad(false, SinAsignaciones, mensaje, MotivoInfactibilidad.Otro);
        }

        /// <summary>
        /// Reintenta el solve una vez por cada grupo con sesiones propias, excluyéndolo, para
        /// identificar cuáles son responsables de una infactibilidad que ningún pre-check
        /// estructural explicó. Solo se invoca desde el catch-all final, con permitirSweep=false
        /// en las resoluciones recursivas (nunca dispara un segundo barrido). Devuelve null si el
        /// barrido no corrió (deshabilitado o demasiados grupos candidatos).
        /// </summary>
        private List<string>? EjecutarSweepDiagnostico(
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

            var responsables = new List<string>();
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
                        responsables.Add(candidato.Nombre);
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
