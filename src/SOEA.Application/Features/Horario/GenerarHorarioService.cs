using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Orquesta el pipeline de 3 fases para generar un horario académico.
    /// Fase 1 — GraphColoring (pre-asignación de bloques sin conflictos).
    /// Fase 2 — CP-SAT ConstraintProgramming (factibilidad con restricciones duras).
    /// Fase 3 — Genetic Algorithm (optimización de restricciones blandas).
    /// </summary>
    public class GenerarHorarioService
    {
        private readonly IMotorColoracionGrafo       _fase1;
        private readonly IMotorConstraintProgramming  _fase2;
        private readonly IMotorGenetico              _fase3;
        private readonly IHorarioRepositorio         _horarioRepo;
        private readonly ISesionRepositorio          _sesionRepo;
        private readonly IAsignacionSemanalRepositorio _asignacionRepo;
        private readonly IUnitOfWork                 _uow;

        public GenerarHorarioService(
            IMotorColoracionGrafo       fase1,
            IMotorConstraintProgramming  fase2,
            IMotorGenetico              fase3,
            IHorarioRepositorio         horarioRepo,
            ISesionRepositorio          sesionRepo,
            IAsignacionSemanalRepositorio asignacionRepo,
            IUnitOfWork                 uow)
        {
            _fase1       = fase1;
            _fase2       = fase2;
            _fase3       = fase3;
            _horarioRepo = horarioRepo;
            _sesionRepo  = sesionRepo;
            _asignacionRepo = asignacionRepo;
            _uow         = uow;
        }

        public async Task<GenerarHorarioResponse> EjecutarAsync(GenerarHorarioRequest request, CancellationToken ct = default)
        {
            var logs = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            logs.Add($"[INFO] Iniciando pipeline de optimización con {request.Asignaturas.Count} asignaturas, {request.Docentes.Count} docentes y {request.Espacios.Count} espacios.");

            // ── 1. Convertir DTOs del frontend a entidades de dominio ───────────
            var espacios  = MapearEspacios(request.Espacios);
            var bloques   = GenerarBloquesTiempo();
            var docentes  = MapearDocentes(request.Docentes, bloques);

            // CR-08: cohorte implícita — todas las materias de este run pertenecen al mismo grupo
            // de estudiantes y deben serializarse (un grupo no puede estar en dos sesiones a la vez).
            // "Un run = un grupo": si el request trae un grupo, usamos SU Id como cohorteId para que
            // sus sesiones crucen con la disponibilidad declarada (HC-G01). Sin grupo → id sintético
            // (comportamiento previo: serialización de cohorte sin restricción de franja).
            var cohorteId = request.Grupos
                .Select(g => Guid.TryParse(g.Id, out var gid) ? gid : (Guid?)null)
                .FirstOrDefault(g => g.HasValue) ?? Guid.NewGuid();

            // SC-PRES: mapa de categoria por asignatura para priorizar asignación presencial.
            // Obligatoria > Optativa > Electiva (se virtualiza en orden inverso si hay saturación).
            var categoriaPorAsig = request.Asignaturas
                .ToDictionary(
                    dto => Guid.TryParse(dto.Id, out var aid) ? aid : Guid.Empty,
                    dto => ParseCategoria(dto.Categoria));

            // HC-VH: ventana horaria por asignatura (la fija Secretaría Académica). Se pasa a CP-SAT
            // como hard constraint — ninguna sesión se asigna fuera de [HoraInicioMin, HoraFinMax].
            var ventanaPorAsig = request.Asignaturas
                .ToDictionary(
                    dto => Guid.TryParse(dto.Id, out var aid) ? aid : Guid.Empty,
                    dto => (ParseHora(dto.HoraInicioMin), ParseHora(dto.HoraFinMax)));

            var sesiones = MapearSesionesIniciales(request.Asignaturas, cohorteId);
            logs.Add($"[INFO] Sesiones creadas a partir de asignaturas: {sesiones.Count}.");

            // ── 1b. Sesiones fijas (horario base) — se añaden con bloque pre-asignado ──
            var sesionesFijasIds = new HashSet<Guid>();
            if (request.SesionesFijas is { Count: > 0 })
            {
                var fijas = MapearSesionesFijas(request.SesionesFijas, bloques, cohorteId);
                sesionesFijasIds = fijas.Select(s => s.Id).ToHashSet();
                sesiones.AddRange(fijas);
                logs.Add($"[INFO] {fijas.Count} sesión(es) fijas del horario base añadidas.");
            }

            // ── 1c. SC-PRES: presencial-first — virtualizar por prioridad si hay saturación ──
            // Si la demanda presencial supera la capacidad estimada, se virtualizan sesiones
            // en orden: Electiva → Optativa → Obligatoria (nunca se toca lo que el usuario
            // marcó como virtual desde el principio).
            int downgradeados = AplicarPrioridadPresencial(sesiones, espacios, categoriaPorAsig);
            if (downgradeados > 0)
                logs.Add($"[INFO] SC-PRES: {downgradeados} sesión(es) cedieron presencialidad por saturación de espacios. " +
                         "Prioridad de cesión — nivel 1: 2ª sesión de materias con 2 sesiones/sem antes que materias " +
                         "de sesión única; nivel 2: Electiva → Optativa → Obligatoria. Se alterna (\"Tipo C\" dinámico: " +
                         "presencial 1 semana) antes de virtualizar por completo.");

            // ── 2. Fase 1 — Coloración de Grafo (pre-asignación de bloques) ────
            logs.Add("[INFO] Fase 1: Pre-procesamiento (Coloración de grafos) iniciada.");
            var swFase1 = System.Diagnostics.Stopwatch.StartNew();
            var sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(sesiones, bloques, ct)).ToList();
            swFase1.Stop();
            logs.Add($"[INFO] Fase 1 completada en {swFase1.ElapsedMilliseconds}ms.");

            // ── 3. Fase 2 — Constraint Programming (factibilidad) ─────────────
            // HC-G01: se pasan los grupos para que CP-SAT aplique la disponibilidad horaria
            // como restricción dura (presencial-first: el pipeline se optimiza alrededor del
            // horario de los grupos, no de los docentes).
            logs.Add("[INFO] Fase 2: Viabilidad (CP-SAT) iniciada.");
            var swFase2 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                sesionesColoreadas, bloques, espacios, docentes,
                grupos: MapearGrupos(request.Grupos),
                sesionesFijasIds: sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
                ventanaPorAsignatura: ventanaPorAsig,
                ct);
            swFase2.Stop();

            if (!resultadoFactibilidad.EsFactible)
            {
                logs.Add($"[ERROR] Fase 2 falló en {swFase2.ElapsedMilliseconds}ms: {resultadoFactibilidad.MensajeError}");
                return new GenerarHorarioResponse
                {
                    HorarioId     = Guid.NewGuid(),
                    Semestre      = request.Semestre,
                    EsFactible    = false,
                    MensajeError  = resultadoFactibilidad.MensajeError,
                    Logs          = logs,
                    Sesiones      = new List<SesionGeneradaDto>()
                };
            }
            logs.Add($"[INFO] Fase 2 completada exitosamente en {swFase2.ElapsedMilliseconds}ms.");

            // ── 4. Fase 3 — Algoritmo Genético (optimización de restricciones blandas) ──
            // Optimiza objetivos del docente (huecos, > 6 horas seguidas, balance entre días
            // disponibles) moviendo el inicio de cada sesión, compartido por las semanas A/B.
            // Preserva todas las restricciones duras de la Fase 2; ante cualquier duda hace
            // fallback interno a la solución de Fase 2.
            logs.Add("[INFO] Fase 3: Optimización (Algoritmo Genético) iniciada.");
            // SC-PRES: info por asignatura (sesiones/semana reales + categoría) para que el fitness
            // penalice proporcionalmente las sesiones que cedieron presencialidad (ver EvaluadorFitness).
            var infoAsignatura = sesionesColoreadas
                .GroupBy(s => s.AsignaturaId)
                .ToDictionary(
                    g => g.Key,
                    g => (g.Count(),
                          categoriaPorAsig.TryGetValue(g.Key, out var cat) ? cat : CategoriaAsignatura.Obligatoria));

            var swFase3 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoGA = await _fase3.OptimizarAsync(
                sesionesColoreadas, resultadoFactibilidad.Asignaciones, bloques, espacios, docentes,
                grupos: MapearGrupos(request.Grupos),
                config: MapearConfiguracion(request.Configuracion),
                infoAsignatura: infoAsignatura,
                ct: ct);
            swFase3.Stop();
            logs.Add($"[INFO] Fase 3 completada en {swFase3.ElapsedMilliseconds}ms. Fitness={resultadoGA.PuntajeFitness}, " +
                     $"generaciones={resultadoGA.Generaciones}, fallback={resultadoGA.UsoFallback}.");

            var asignaciones   = resultadoGA.AsignacionesOptimizadas;
            var puntajeFitness = resultadoGA.PuntajeFitness;

            // ── 4b. Post-chequeo de restricciones duras (P0.3 + P1.8 auditoría) ─────────
            // Verificamos las asignaciones FINALES: HC-I01/HC-S01 (solapes, consciente de duración
            // y semana) y HC-I03 (horas semanales por docente). Si el GA introdujo cualquier
            // violación, hacemos fallback a las asignaciones factibles de la Fase 2.
            var sesionPorIdValidacion = sesionesColoreadas.ToDictionary(s => s.Id);
            var bloqueIndex = Enumerable.Range(0, bloques.Count).ToDictionary(i => bloques[i].Id, i => i);

            var conflictos = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex);
            if (conflictos.Count > 0)
            {
                logs.Add($"[WARN] Post-chequeo detectó {conflictos.Count} violación(es) en la salida del GA; usando la solución de Fase 2.");
                asignaciones   = resultadoFactibilidad.Asignaciones;
                puntajeFitness = 0m;
                conflictos     = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex);
            }

            if (conflictos.Count > 0)
            {
                // Ni siquiera la solución de Fase 2 valida (no debería ocurrir): no publicar.
                logs.Add($"[ERROR] La solución de Fase 2 viola {conflictos.Count} restricción(es) dura(s).");
                foreach (var c in conflictos.Take(20)) logs.Add($"[ERROR] {c}");
                return new GenerarHorarioResponse
                {
                    HorarioId    = Guid.NewGuid(),
                    Semestre     = request.Semestre,
                    EsFactible   = false,
                    MensajeError = $"El horario generado viola {conflictos.Count} restricción(es) dura(s) y no puede publicarse. " +
                                   string.Join(" ", conflictos.Take(5)),
                    Logs         = logs,
                    Sesiones     = new List<SesionGeneradaDto>()
                };
            }
            logs.Add("[INFO] Post-chequeo OK: 0 violaciones de restricciones duras.");

            // ── 5. Persistir sesiones lógicas, Horario y asignaciones semanales ─
            // Las tres escrituras comparten el mismo DbContext (scoped), así que van en UNA
            // transacción: si la última falla, no quedan sesiones ni horario huérfanos sin
            // asignaciones. Antes eran tres SaveChanges independientes (auditoría dotnet).
            var horario = new Domain.Entities.Horario(
                id: Guid.NewGuid(),
                semestre: request.Semestre,
                sesionIds: sesionesColoreadas.Select(s => s.Id).ToList(),
                violacionesRestriccionesDuras: conflictos.Count,
                puntajeFitness: puntajeFitness);

            await _uow.BeginTransactionAsync();
            try
            {
                await _sesionRepo.AddRangeAsync(sesionesColoreadas);
                await _horarioRepo.AddAsync(horario);
                await _asignacionRepo.AddRangeAsync(asignaciones);
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            stopwatch.Stop();
            logs.Add($"[INFO] Pipeline total ejecutado en {stopwatch.ElapsedMilliseconds}ms.");

            // ── 6. Mapear asignaciones al DTO de respuesta (una DTO por semana) ─
            var sesionPorId = sesionesColoreadas.ToDictionary(s => s.Id);
            // Lab de origen por sesión = espacio de su asignación presencial. Permite al frontend
            // ubicar la fila virtual (EspacioId=null) en el laboratorio donde la sesión es presencial.
            var espacioHogarPorSesion = asignaciones
                .Where(a => a.Modalidad == Modalidad.Presencial && a.EspacioId.HasValue)
                .GroupBy(a => a.SesionId)
                .ToDictionary(g => g.Key, g => g.First().EspacioId!.Value.ToString());
            var bloquePorId = bloques.ToDictionary(b => b.Id);
            var sesionesDto = asignaciones
                .Where(a => sesionPorId.ContainsKey(a.SesionId))
                .Select(a => MapearSesionDto(
                    a, sesionPorId[a.SesionId], bloquePorId,
                    espacioHogarPorSesion.GetValueOrDefault(a.SesionId)))
                .ToList();

            return new GenerarHorarioResponse
            {
                HorarioId      = horario.Id,
                Semestre       = request.Semestre,
                EsFactible     = true,
                PuntajeFitness = puntajeFitness,
                Generaciones   = resultadoGA.Generaciones,
                Logs           = logs,
                Sesiones       = sesionesDto
            };
        }

        /// <summary>
        /// Post-chequeo de restricciones duras sobre las asignaciones finales:
        /// HC-C01 (solapes de cohorte) + HC-S01 (solapes de espacio). El docente está fuera del
        /// pipeline (CR-08), así que ya no se valida carga semanal de docente (HC-I03).
        /// </summary>
        private static IReadOnlyList<string> Validar(
            IReadOnlyList<AsignacionSemanal> asignaciones,
            IReadOnlyDictionary<Guid, Sesion> sesionPorId,
            IReadOnlyDictionary<Guid, int> bloqueIndex)
        {
            return ValidadorRestriccionesDuras.Validar(asignaciones, sesionPorId, bloqueIndex);
        }

        // ── Helpers de mapeo ─────────────────────────────────────────────────────

        /// <summary>
        /// Mapea las sesiones del horario base a entidades de dominio con el BloqueTiempo ya
        /// pre-asignado (buscando el bloque por día+horaInicio en la grilla canónica).
        /// Si no se encuentra un bloque coincidente, la sesión se omite silenciosamente.
        /// </summary>
        private static List<Sesion> MapearSesionesFijas(
            List<SesionFijaDto> dtos,
            List<BloqueTiempo> bloques,
            Guid cohorteId)
        {
            // Índice rápido: (dia, horaInicio) → BloqueTiempo
            var bloqueDict = bloques.ToDictionary(
                b => (DiaToString(b.Dia), b.HoraInicio.ToString("HH:mm")));

            var resultado = new List<Sesion>();
            foreach (var dto in dtos)
            {
                if (!bloqueDict.TryGetValue((dto.Dia.ToLowerInvariant(), dto.HoraInicio), out var bloque))
                    continue;

                var asigId  = Guid.TryParse(dto.AsignaturaId, out var aid) ? aid : Guid.NewGuid();
                Guid? espId = Guid.TryParse(dto.EspacioId, out var eid) ? eid : null;

                var alternancia = dto.Alternancia?.Trim().ToLowerInvariant() switch
                {
                    "tipoa"          => TipoAlternancia.TipoA,
                    "tipob"          => TipoAlternancia.TipoB,
                    "sinalternancia" => TipoAlternancia.SinAlternancia,
                    _                => TipoAlternancia.SinAlternancia
                };

                var sesion = new Sesion(
                    id: Guid.NewGuid(),
                    asignaturaId: asigId,
                    docenteId: null,
                    bloqueId: bloque.Id,
                    espacioId: espId,
                    grupoId: cohorteId,
                    alternancia: alternancia,
                    modalidad: dto.Virtual ? Modalidad.Virtual : Modalidad.Presencial,
                    duracionHoras: dto.DuracionHoras > 0 ? dto.DuracionHoras : 2m,
                    esBloque: false,
                    estaDividida: false);

                // Marca la sesión como ya asignada para que la Fase 1 (ColoracionGrafo)
                // no la reasigne. CP-SAT recibirá su ID en sesionesFijasIds y le añadirá
                // una restricción de igualdad (no solo un hint).
                sesion.AsignarBloqueTiempo(bloque.Id);
                if (espId.HasValue) sesion.AsignarEspacio(espId.Value);

                resultado.Add(sesion);
            }
            return resultado;
        }

        private static List<Espacio> MapearEspacios(List<EspacioDto> dtos) =>
            dtos.Select(dto => new Espacio(
                id: Guid.TryParse(dto.Id, out var eid) ? eid : Guid.NewGuid(),
                nombre: dto.Nombre,
                tipo: ParseTipoEspacio(dto.Tipo),
                capacidad: dto.Capacidad > 0 ? dto.Capacidad : 30
            )).ToList();

        private static List<Docente> MapearDocentes(List<DocenteDto> dtos, List<BloqueTiempo> bloques)
        {
            return dtos.Select(dto =>
            {
                var id = Guid.TryParse(dto.Id, out var did) ? did : Guid.NewGuid();
                var maxHoras = dto.MaxHoras.HasValue && dto.MaxHoras > 0 ? dto.MaxHoras.Value : 20m;

                var franjas = new HashSet<FranjaHoraria>();
                var bloquesDisponibles = new List<BloqueTiempo>();

                foreach (var (diaNombre, dispDia) in dto.Disponibilidad)
                {
                    if (dispDia.NoDisponible) continue;

                    var dia = ParseDiaSemana(diaNombre);
                    if (dia == null) continue;

                    TimeOnly? desde = null;
                    TimeOnly? hasta = null;

                    if (!string.IsNullOrWhiteSpace(dispDia.Desde) && TimeOnly.TryParse(dispDia.Desde, out var d))
                        desde = d;
                    if (!string.IsNullOrWhiteSpace(dispDia.Hasta) && TimeOnly.TryParse(dispDia.Hasta, out var h))
                        hasta = h;

                    if (desde == null && hasta == null && !string.IsNullOrWhiteSpace(dispDia.FranjaGeneral))
                    {
                        // Frontend sends the full label, e.g. "Matutino (6:00–12:00)".
                        // Use StartsWith so both the short key and the full label match.
                        if (dispDia.FranjaGeneral.StartsWith("Matutino", StringComparison.OrdinalIgnoreCase))
                        {
                            desde = new TimeOnly(6, 0);
                            hasta = new TimeOnly(13, 0);
                            franjas.Add(FranjaHoraria.Matutino);
                        }
                        else if (dispDia.FranjaGeneral.StartsWith("Vespertino", StringComparison.OrdinalIgnoreCase))
                        {
                            desde = new TimeOnly(13, 0);
                            hasta = new TimeOnly(20, 0);
                            franjas.Add(FranjaHoraria.Vespertino);
                        }
                        else if (dispDia.FranjaGeneral.StartsWith("Nocturno", StringComparison.OrdinalIgnoreCase))
                        {
                            desde = new TimeOnly(18, 0);
                            hasta = new TimeOnly(22, 0);
                            franjas.Add(FranjaHoraria.Vespertino);
                        }
                        else
                        {
                            franjas.Add(FranjaHoraria.Matutino);
                            franjas.Add(FranjaHoraria.Vespertino);
                        }
                    }
                    else
                    {
                        franjas.Add(FranjaHoraria.Matutino);
                        franjas.Add(FranjaHoraria.Vespertino);
                    }

                    var bloquesDia = bloques.Where(b => b.Dia == dia.Value);
                    if (desde.HasValue) bloquesDia = bloquesDia.Where(b => b.HoraInicio >= desde.Value);
                    if (hasta.HasValue) bloquesDia = bloquesDia.Where(b => b.HoraFin <= hasta.Value);

                    foreach (var bloque in bloquesDia)
                        bloquesDisponibles.Add(bloque);
                }

                // If the DTO sends no availability data, fall back to unrestricted (both franjas).
                var disponibilidadFinal = franjas.Count > 0
                    ? franjas.ToList()
                    : new List<FranjaHoraria> { FranjaHoraria.Matutino, FranjaHoraria.Vespertino };

                // P1.3 auditoría: distinguir "sin información" de "explícitamente no disponible".
                // Solo aplicamos el fallback "todos los bloques" cuando el DTO NO trae ninguna
                // información de disponibilidad. Si el usuario configuró días (todos NoDisponible
                // o con horarios que no calzan con la grilla), respetamos esa restricción: la
                // lista queda vacía y la Fase 2 (HC-I02) reportará infactible con un mensaje claro,
                // en vez de agendar silenciosamente a un docente marcado como no disponible.
                bool sinInformacionDisponibilidad = dto.Disponibilidad.Count == 0;
                if (bloquesDisponibles.Count == 0 && sinInformacionDisponibilidad)
                    bloquesDisponibles.AddRange(bloques);

                var docente = new Docente(
                    id: id,
                    nombre: dto.Nombre,
                    apellido: string.Empty,
                    correo: $"docente-{id}@soea.edu",
                    maximoHorasSemanales: maxHoras,
                    disponibilidad: disponibilidadFinal);

                foreach (var bloque in bloquesDisponibles)
                    docente.AgregarBloqueDisponibilidad(bloque);

                return docente;
            }).ToList();
        }

        private static DiaDeSemana? ParseDiaSemana(string dia) => dia.Trim().ToLowerInvariant() switch
        {
            "lunes"      => DiaDeSemana.Lunes,
            "martes"     => DiaDeSemana.Martes,
            "miercoles"  => DiaDeSemana.Miercoles,
            "miércoles"  => DiaDeSemana.Miercoles,
            "jueves"     => DiaDeSemana.Jueves,
            "viernes"    => DiaDeSemana.Viernes,
            "sabado"     => DiaDeSemana.Sábado,
            "sábado"     => DiaDeSemana.Sábado,
            _            => null
        };

        private static List<Sesion> MapearSesionesIniciales(
            List<AsignaturaDto> asignaturasDtos,
            Guid cohorteId)
        {
            var sesiones = new List<Sesion>();
            // Bloque placeholder — Fase 1 lo reemplazará
            var bloqueTemp = Guid.NewGuid();

            foreach (var dto in asignaturasDtos)
            {
                var asigId   = Guid.TryParse(dto.Id, out var aid) ? aid : Guid.NewGuid();

                // CR-08 / presencial-first: el docente sale del pipeline (se asigna DESPUÉS de
                // generar el horario). Las sesiones se generan sin docente; el eje de conflicto
                // y de optimización es la cohorte (GrupoId), no el docente.

                // Fix #5: case-insensitive alternancia matching
                var alternancia = dto.Alternancia?.Trim().ToLowerInvariant() switch
                {
                    "tipoa"          => TipoAlternancia.TipoA,
                    "tipob"          => TipoAlternancia.TipoB,
                    "sinalternancia" => TipoAlternancia.SinAlternancia,
                    _                => TipoAlternancia.SinAlternancia
                };

                var modalidad = dto.EsVirtual ? Modalidad.Virtual : Modalidad.Presencial;

                // Prefer explicit HorasPorSesion/SesionesPorSemana when the frontend sends them;
                // fall back to deriving from HorasSemanales (÷2) for backwards compatibility.
                int sesionesASolicitar;
                decimal duracionPorSesion;

                if (dto.HorasPorSesion.GetValueOrDefault() > 0 && dto.SesionesPorSemana.GetValueOrDefault() > 0)
                {
                    sesionesASolicitar = dto.SesionesPorSemana.GetValueOrDefault();
                    duracionPorSesion  = dto.HorasPorSesion.GetValueOrDefault();
                }
                else
                {
                    var horas = dto.HorasSemanales.GetValueOrDefault() > 0 ? dto.HorasSemanales.GetValueOrDefault()
                              : dto.Creditos.GetValueOrDefault() > 0       ? (decimal)dto.Creditos.GetValueOrDefault()
                              : 2m;
                    if (horas > 8) horas = 8m;
                    sesionesASolicitar = (int)Math.Ceiling(horas / 2m);
                    duracionPorSesion  = Math.Round(horas / sesionesASolicitar, 1);
                }

                for (int i = 0; i < sesionesASolicitar; i++)
                {
                    // HC-S05: si la asignatura tiene espacio fijo, se lo pasamos a la sesión
                // para que CP-SAT lo respete como hard constraint (solo ese espacio).
                Guid? espacioFijo = !string.IsNullOrWhiteSpace(dto.EspacioFijoId) &&
                                    Guid.TryParse(dto.EspacioFijoId, out var efid) ? efid : null;

                sesiones.Add(new Sesion(
                        id: Guid.NewGuid(),
                        asignaturaId: asigId,
                        docenteId: null,
                        bloqueId: bloqueTemp,
                        espacioId: espacioFijo,
                        grupoId: cohorteId,
                        alternancia: alternancia,
                        modalidad: modalidad,
                        duracionHoras: duracionPorSesion,
                        esBloque: false,
                        estaDividida: false));
                }
            }
            return sesiones;
        }

        /// <summary>
        /// Genera la grilla canónica de bloques de tiempo institucional.
        /// Lunes–Viernes 06:00–22:00 en bloques de 1 hora; Sábado 06:00–13:00.
        /// </summary>
        private static List<BloqueTiempo> GenerarBloquesTiempo()
        {
            var bloques = new List<BloqueTiempo>();
            var dias    = new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles,
                                  DiaDeSemana.Jueves, DiaDeSemana.Viernes };

            foreach (var dia in dias)
            {
                for (int h = 6; h < 22; h++)
                {
                    bloques.Add(new BloqueTiempo(
                        Guid.NewGuid(), dia,
                        new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
                }
            }

            // Sábado: 06:00–13:00
            for (int h = 6; h < 13; h++)
            {
                bloques.Add(new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Sábado,
                    new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
            }

            return bloques;
        }

        private static SesionGeneradaDto MapearSesionDto(
            AsignacionSemanal a, Sesion s, IReadOnlyDictionary<Guid, BloqueTiempo> bloquePorId, string? espacioIdHogar)
        {
            bloquePorId.TryGetValue(a.BloqueTiempoId, out var bloque);

            string horaInicio = "07:00";
            string horaFin    = "09:00";
            string dia        = "lunes";

            if (bloque != null)
            {
                dia        = DiaToString(bloque.Dia);
                horaInicio = bloque.HoraInicio.ToString("HH:mm");
                // HoraFin = HoraInicio + DuracionHoras (la duración es input fijo, no la del bloque atómico).
                horaFin    = bloque.HoraInicio.AddHours((double)s.DuracionHoras).ToString("HH:mm");
            }

            return new SesionGeneradaDto
            {
                Id            = s.Id.ToString(),
                AsignaturaId  = s.AsignaturaId.ToString(),
                DocenteId     = s.DocenteId?.ToString() ?? string.Empty,
                EspacioId     = a.EspacioId?.ToString(),
                EspacioIdHogar = espacioIdHogar ?? a.EspacioId?.ToString(),
                Dia           = dia,
                HoraInicio    = horaInicio,
                HoraFin       = horaFin,
                DuracionHoras = s.DuracionHoras,
                Alternancia   = s.Alternancia.ToString(),
                Virtual       = a.Modalidad == Modalidad.Virtual,
                Semana        = a.Semana.ToString()
            };
        }

        private static ConfiguracionOptimizacion MapearConfiguracion(ConfiguracionAlgoritmoDto? dto) =>
            dto is null
                ? new ConfiguracionOptimizacion()
                : new ConfiguracionOptimizacion(
                    TamañoPoblacion:      dto.TamañoPoblacion,
                    MaxGeneraciones:      dto.MaxGeneraciones,
                    ProbabilidadMutacion: dto.ProbabilidadMutacion,
                    ProbabilidadCruce:    dto.ProbabilidadCruce,
                    UmbralConvergencia:   dto.UmbralConvergencia,
                    PesoErgo:             dto.PesoErgo,
                    PesoTiempos:          dto.PesoTiempos,
                    PesoAlmuerzo:         dto.PesoAlmuerzo);

        /// <summary>
        /// Convierte los GrupoDtos del request a entidades de dominio Grupo con su disponibilidad.
        /// Los grupos informan HC-G01 en CP-SAT: si Disponibilidad no está vacía, el solver
        /// solo asignará sus sesiones en bloques dentro de esa franja.
        /// </summary>
        private static List<Grupo> MapearGrupos(List<GrupoDto> dtos)
        {
            var grupos = new List<Grupo>();
            foreach (var dto in dtos)
            {
                if (!Guid.TryParse(dto.Id, out var id)) continue;

                var disponibilidad = dto.Disponibilidad
                    .Select(s => s.Trim().ToLowerInvariant() switch
                    {
                        "matutino"   => (FranjaHoraria?)FranjaHoraria.Matutino,
                        "vespertino" => (FranjaHoraria?)FranjaHoraria.Vespertino,
                        _            => null
                    })
                    .Where(f => f.HasValue)
                    .Select(f => f!.Value)
                    .Distinct()
                    .ToList();

                Guid? asigId    = Guid.TryParse(dto.AsignaturaId, out var aid) ? aid : null;
                Guid? facId     = Guid.TryParse(dto.FacultadId,   out var fid) ? fid : null;

                var grupo = new Grupo(
                    id:                  id,
                    nombre:              dto.Nombre,
                    programaId:          Guid.Empty,   // no requerido para el pipeline
                    semestre:            1,
                    estudiantesInscritos: Math.Max(1, dto.EstudiantesInscritos),
                    disponibilidad:      disponibilidad,
                    codigo:              dto.Codigo,
                    asignaturaId:        asigId,
                    facultadId:          facId);

                grupo.ActualizarDisponibilidadUi(dto.DisponibilidadUiJson);
                grupos.Add(grupo);
            }
            return grupos;
        }

        private static string DiaToString(DiaDeSemana dia) => dia switch
        {
            DiaDeSemana.Lunes     => "lunes",
            DiaDeSemana.Martes    => "martes",
            DiaDeSemana.Miercoles => "miercoles",
            DiaDeSemana.Jueves    => "jueves",
            DiaDeSemana.Viernes   => "viernes",
            DiaDeSemana.Sábado    => "sabado",
            _ => "lunes"
        };

        private static TipoEspacio ParseTipoEspacio(string? tipo) => tipo?.ToLower() switch
        {
            "laboratorio" => TipoEspacio.Laboratorio,
            "auditorio"   => TipoEspacio.Auditorio,
            _             => TipoEspacio.Salon
        };

        private static TimeOnly? ParseHora(string? hhmm) =>
            !string.IsNullOrWhiteSpace(hhmm) && TimeOnly.TryParse(hhmm, out var t) ? t : null;

        private static CategoriaAsignatura ParseCategoria(string? categoria) =>
            categoria?.Trim().ToLowerInvariant() switch
            {
                "optativa"   => CategoriaAsignatura.Optativa,
                "electiva"   => CategoriaAsignatura.Electiva,
                _            => CategoriaAsignatura.Obligatoria   // conservador: si no se especifica → Obligatoria
            };

        /// <summary>
        /// SC-PRES: cuando la demanda presencial supera la capacidad estimada de espacios,
        /// virtualiza sesiones en orden de menor a mayor prioridad:
        ///   Electiva → Optativa → Obligatoria
        /// Solo actúa sobre sesiones que el usuario NO marcó como virtuales desde el principio
        /// (EsVirtual=true en el DTO). Retorna cuántas sesiones fueron convertidas.
        /// </summary>
        // internal (no private) para verificación directa de la prioridad de cesión en SOEA.Tests.
        internal static int AplicarPrioridadPresencial(
            List<Sesion> sesiones,
            List<Espacio> espacios,
            Dictionary<Guid, CategoriaAsignatura> categoriaPorAsig)
        {
            if (espacios.Count == 0) return 0; // sin espacios → todas son virtuales por naturaleza

            // Capacidad máxima estimada: nro_espacios × días × horas_útiles. Heurística de pre-pase;
            // el gate duro real es CP-SAT (HC-CAP / demanda-vs-capacidad por semana).
            int capacidadMaxEstimada = espacios.Count * 5 * 8;
            int Horas(Sesion s) => Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));

            int demandaHoras = sesiones.Where(s => s.Modalidad == Modalidad.Presencial).Sum(Horas);
            int excesohoras  = demandaHoras - capacidadMaxEstimada;
            if (excesohoras <= 0) return 0; // sin saturación → no tocar nada

            // Sesiones por asignatura (cohorte implícita = 1 grupo/run ⇒ = sesiones/semana).
            var totalPorAsig = sesiones
                .GroupBy(s => s.AsignaturaId)
                .ToDictionary(g => g.Key, g => g.Count());

            // Nivel 2 (desempate por categoría): Electiva cede primero, Obligatoria última.
            int CategoriaRank(Sesion s) =>
                categoriaPorAsig.TryGetValue(s.AsignaturaId, out var cat)
                    ? cat switch
                    {
                        CategoriaAsignatura.Electiva => 0,
                        CategoriaAsignatura.Optativa => 1,
                        _                            => 2
                    }
                    : 2;

            // Nivel 1 (estructural): asignatura con ≥2 sesiones cede primero (0); con 1 sesión, último (1).
            int EstructuralRank(Sesion s) =>
                totalPorAsig.TryGetValue(s.AsignaturaId, out var n) && n >= 2 ? 0 : 1;

            // Orden de cesión: estructural y luego categoría (ascendente ⇒ cede primero).
            List<Sesion> OrdenarCesion(IEnumerable<Sesion> ss) =>
                ss.OrderBy(EstructuralRank).ThenBy(CategoriaRank).ToList();

            int cedidas = 0;
            var tiposAB = new[] { TipoAlternancia.TipoA, TipoAlternancia.TipoB };
            var patronDeTipo = new Dictionary<TipoAlternancia, Guid>
            {
                [TipoAlternancia.TipoA] = TipoAlternanciaConfig.IdTipoA,
                [TipoAlternancia.TipoB] = TipoAlternanciaConfig.IdTipoB
            };

            // ── Pase 1 — "Tipo C" dinámico: alternar (presencial 1 semana) en vez de virtualizar.
            // Solo asignaturas con ≥2 sesiones y conservando SIEMPRE ≥1 sesión presencial pura por
            // asignatura. Respeta Bloqueada. Alterna A/B en zigzag para repartir la huella entre semanas.
            var presencialesPurasPorAsig = new Dictionary<Guid, int>(totalPorAsig);
            int ab = 0;
            foreach (var s in OrdenarCesion(sesiones.Where(s =>
                         s.Modalidad == Modalidad.Presencial &&
                         s.Alternancia == TipoAlternancia.SinAlternancia &&
                         !s.Bloqueada)))
            {
                if (excesohoras <= 0) break;
                if (!totalPorAsig.TryGetValue(s.AsignaturaId, out var n) || n < 2) continue; // single → pase 2
                if (presencialesPurasPorAsig[s.AsignaturaId] <= 1) continue;                  // conserva ≥1 presencial

                var tipo = tiposAB[ab++ % 2];
                s.AplicarAlternancia(tipo, patronDeTipo[tipo]);
                presencialesPurasPorAsig[s.AsignaturaId]--;
                excesohoras -= (Horas(s) + 1) / 2; // alterna ⇒ ~la mitad de huella presencial (heurística)
                cedidas++;
            }

            // ── Pase 2 — último recurso: virtualización total, mismo orden de prioridad.
            // Aquí sí pueden caer las sesiones únicas (single-session), solo si alternar no bastó.
            foreach (var s in OrdenarCesion(sesiones.Where(s =>
                         s.Modalidad == Modalidad.Presencial && !s.Bloqueada)))
            {
                if (excesohoras <= 0) break;
                s.VirtualizarSesion();
                excesohoras -= Horas(s);
                cedidas++;
            }

            return cedidas;
        }
    }
}
