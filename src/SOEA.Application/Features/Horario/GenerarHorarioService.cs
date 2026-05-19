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

        public GenerarHorarioService(
            IMotorColoracionGrafo       fase1,
            IMotorConstraintProgramming  fase2,
            IMotorGenetico              fase3,
            IHorarioRepositorio         horarioRepo,
            ISesionRepositorio          sesionRepo)
        {
            _fase1       = fase1;
            _fase2       = fase2;
            _fase3       = fase3;
            _horarioRepo = horarioRepo;
            _sesionRepo  = sesionRepo;
        }

        public async Task<GenerarHorarioResponse> EjecutarAsync(GenerarHorarioRequest request)
        {
            var logs = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            logs.Add($"[INFO] Iniciando pipeline de optimización con {request.Asignaturas.Count} asignaturas, {request.Docentes.Count} docentes y {request.Espacios.Count} espacios.");

            var primeraAsig = request.Asignaturas.FirstOrDefault();
            if (primeraAsig != null)
            {
                logs.Add($"[DEBUG] Primera Asignatura Ingresada: {primeraAsig.Nombre} (ID: {primeraAsig.Id})");
                logs.Add($"[DEBUG] -> Créditos: {primeraAsig.Creditos}, HorasSemanales: {primeraAsig.HorasSemanales}, Alternancia: {primeraAsig.Alternancia}, Virtual: {primeraAsig.EsVirtual}");
            }

            // ── 1. Convertir DTOs del frontend a entidades de dominio ───────────
            var espacios  = MapearEspacios(request.Espacios);
            var bloques   = GenerarBloquesTiempo();
            var docentes  = MapearDocentes(request.Docentes, bloques);

            var sesiones = MapearSesionesIniciales(request.Asignaturas, docentes);
            logs.Add($"[INFO] Sesiones creadas a partir de asignaturas: {sesiones.Count}.");

            // ── 2. Fase 1 — Coloración de Grafo (pre-asignación de bloques) ────
            logs.Add("[INFO] Fase 1: Pre-procesamiento (Coloración de grafos) iniciada.");
            var swFase1 = System.Diagnostics.Stopwatch.StartNew();
            var sesionesColoreadas = (await _fase1.AsignarBloquesDeTiempoAsync(sesiones, bloques)).ToList();
            swFase1.Stop();
            logs.Add($"[INFO] Fase 1 completada en {swFase1.ElapsedMilliseconds}ms.");

            // ── 3. Fase 2 — Constraint Programming (factibilidad) ─────────────
            logs.Add("[INFO] Fase 2: Viabilidad (CP-SAT) iniciada.");
            var swFase2 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoFactibilidad = await _fase2.ResolverFactibilidadAsync(
                sesionesColoreadas, bloques, espacios, docentes);
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

            // ── 4. Fase 3 — Algoritmo Genético (optimización soft constraints) ─
            logs.Add("[INFO] Fase 3: Algoritmo Genético (Optimización) iniciado.");
            var swFase3 = System.Diagnostics.Stopwatch.StartNew();
            var resultadoOptimizacion = await _fase3.OptimizarAsync(
                resultadoFactibilidad.SesionesResueltas, bloques, espacios, docentes);
            swFase3.Stop();
            logs.Add($"[INFO] Fase 3 completada en {swFase3.ElapsedMilliseconds}ms. Fitness final: {resultadoOptimizacion.PuntajeFitness:F2}");

            var sesionesFinales = resultadoOptimizacion.SesionesOptimizadas.ToList();

            // ── 5. Persistir sesiones y Horario en la base de datos ───────────
            await _sesionRepo.AddRangeAsync(sesionesFinales);

            var horario = new Domain.Entities.Horario(
                id: Guid.NewGuid(),
                semestre: request.Semestre,
                sesionIds: sesionesFinales.Select(s => s.Id).ToList(),
                violacionesRestriccionesDuras: 0,
                puntajeFitness: resultadoOptimizacion.PuntajeFitness);

            await _horarioRepo.AddAsync(horario);
            
            stopwatch.Stop();
            logs.Add($"[INFO] Pipeline total ejecutado en {stopwatch.ElapsedMilliseconds}ms.");

            // ── 6. Mapear sesiones al DTO de respuesta ────────────────────────
            var sesionesDto = sesionesFinales.Select(s => MapearSesionDto(s, bloques)).ToList();

            return new GenerarHorarioResponse
            {
                HorarioId      = horario.Id,
                Semestre       = request.Semestre,
                EsFactible     = true,
                PuntajeFitness = resultadoOptimizacion.PuntajeFitness,
                Generaciones   = resultadoOptimizacion.Generaciones,
                Logs           = logs,
                Sesiones       = sesionesDto
            };
        }

        // ── Helpers de mapeo ─────────────────────────────────────────────────────

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
                            hasta = new TimeOnly(20, 0);
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
                    sesiones.Add(new Sesion(
                        id: Guid.NewGuid(),
                        asignaturaId: asigId,
                        docenteId: docenteId,
                        bloqueId: bloqueTemp,
                        // Fix #8: null means "not yet assigned" — Phase 2 will assign rooms
                        espacioId: null,
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

        private static SesionGeneradaDto MapearSesionDto(Sesion s, List<BloqueTiempo> bloques)
        {
            var bloque = bloques.FirstOrDefault(b => b.Id == s.BloqueTiempoId);
            return new SesionGeneradaDto
            {
                Id           = s.Id.ToString(),
                AsignaturaId = s.AsignaturaId.ToString(),
                DocenteId    = s.DocenteId.ToString(),
                EspacioId    = s.EspacioId?.ToString(),
                Dia          = bloque != null ? DiaToString(bloque.Dia) : "lunes",
                HoraInicio   = bloque != null ? bloque.HoraInicio.ToString("HH:mm") : "07:00",
                HoraFin      = bloque != null ? bloque.HoraFin.ToString("HH:mm")    : "09:00",
                Alternancia  = s.Alternancia.ToString(),
                Virtual      = s.Modalidad == Modalidad.Virtual
            };
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
    }
}
