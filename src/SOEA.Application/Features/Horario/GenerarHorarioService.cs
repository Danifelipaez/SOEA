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

            var sesiones = MapearSesionesIniciales(request.Asignaturas, docentes);
            logs.Add($"[INFO] Sesiones creadas a partir de asignaturas: {sesiones.Count}.");

            // ── 1b. Sesiones fijas (horario base) — se añaden con bloque pre-asignado ──
            var sesionesFijasIds = new HashSet<Guid>();
            if (request.SesionesFijas is { Count: > 0 })
            {
                var fijas = MapearSesionesFijas(request.SesionesFijas, bloques, docentes);
                sesionesFijasIds = fijas.Select(s => s.Id).ToHashSet();
                sesiones.AddRange(fijas);
                logs.Add($"[INFO] {fijas.Count} sesión(es) fijas del horario base añadidas.");
            }

            // ── 2. Fase 1 — Coloración de Grafo (pre-asignación de bloques) ────
            logs.Add("[INFO] Fase 1: Pre-procesamiento (Coloración de grafos) iniciada.");
            var swFase1 = System.Diagnostics.Stopwatch.StartNew();
            var sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(sesiones, bloques, ct)).ToList();
            swFase1.Stop();
            logs.Add($"[INFO] Fase 1 completada en {swFase1.ElapsedMilliseconds}ms.");

            // ── 3. Fase 2 — Constraint Programming (factibilidad) ─────────────
            logs.Add("[INFO] Fase 2: Viabilidad (CP-SAT) iniciada.");
            var swFase2 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                sesionesColoreadas, bloques, espacios, docentes,
                sesionesFijasIds.Count > 0 ? sesionesFijasIds : null,
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
            var swFase3 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoGA = await _fase3.OptimizarAsync(
                sesionesColoreadas, resultadoFactibilidad.Asignaciones, bloques, espacios, docentes,
                MapearConfiguracion(request.Configuracion), ct);
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
            var bloqueIndex = new Dictionary<Guid, int>(bloques.Count);
            for (int i = 0; i < bloques.Count; i++) bloqueIndex[bloques[i].Id] = i;
            var maxHorasPorDocente = docentes.ToDictionary(d => d.Id, d => d.MaximoHorasSemanales);

            var conflictos = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex, sesionesColoreadas, maxHorasPorDocente);
            if (conflictos.Count > 0)
            {
                logs.Add($"[WARN] Post-chequeo detectó {conflictos.Count} violación(es) en la salida del GA; usando la solución de Fase 2.");
                asignaciones   = resultadoFactibilidad.Asignaciones;
                puntajeFitness = 0m;
                conflictos     = Validar(asignaciones, sesionPorIdValidacion, bloqueIndex, sesionesColoreadas, maxHorasPorDocente);
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
        /// Post-chequeo combinado de restricciones duras sobre las asignaciones finales:
        /// HC-I01/HC-S01 (solapes de docente/espacio) + HC-I03 (horas semanales por docente).
        /// </summary>
        private static IReadOnlyList<string> Validar(
            IReadOnlyList<AsignacionSemanal> asignaciones,
            IReadOnlyDictionary<Guid, Sesion> sesionPorId,
            IReadOnlyDictionary<Guid, int> bloqueIndex,
            IReadOnlyList<Sesion> sesiones,
            IReadOnlyDictionary<Guid, decimal> maxHorasPorDocente)
        {
            var conflictos = new List<string>();
            conflictos.AddRange(ValidadorRestriccionesDuras.Validar(asignaciones, sesionPorId, bloqueIndex));
            conflictos.AddRange(ValidadorRestriccionesDuras.ValidarCargaSemanal(sesiones, maxHorasPorDocente));
            return conflictos;
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
            List<Docente> docentes)
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
                var docId   = Guid.TryParse(dto.DocenteId,    out var did) ? did : Guid.NewGuid();
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
                    docenteId: docId,
                    bloqueId: bloque.Id,
                    espacioId: espId,
                    grupoId: null,
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
            List<Docente> docentes)
        {
            var sesiones = new List<Sesion>();
            // Bloque placeholder — Fase 1 lo reemplazará
            var bloqueTemp = Guid.NewGuid();

            int roundRobinIdx = 0;

            foreach (var dto in asignaturasDtos)
            {
                var asigId   = Guid.TryParse(dto.Id, out var aid) ? aid : Guid.NewGuid();
                var programaId = Guid.TryParse(dto.ProgramaId, out var pid) ? pid : Guid.NewGuid();

                Guid docenteId;
                if (!string.IsNullOrWhiteSpace(dto.DocenteId) &&
                    Guid.TryParse(dto.DocenteId, out var docId) &&
                    docentes.Any(d => d.Id == docId))
                {
                    docenteId = docId;
                }
                else if (docentes.Count > 0)
                {
                    // Round-robin across available docentes to avoid piling all unassigned
                    // sessions onto the first one and blowing past their weekly hour limit.
                    docenteId = docentes[roundRobinIdx % docentes.Count].Id;
                    roundRobinIdx++;
                }
                else
                {
                    docenteId = Guid.NewGuid();
                }

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
                        docenteId: docenteId,
                        bloqueId: bloqueTemp,
                        espacioId: espacioFijo,
                        grupoId: null,
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
                DocenteId     = s.DocenteId.ToString(),
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
    }
}
