using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Asigna (o desasigna) un docente a una sesión ya generada por el pipeline.
    /// HC-I01 (solape de franja) es hard → 409 si viola.
    /// HC-I02 (disponibilidad) y HC-I03 (carga máx.) son soft → advertencias, no rechazo.
    /// Presencial-first: el docente sale del pipeline de generación (CR-02/CR-08);
    /// esta es la única vía para asignarlo después.
    /// </summary>
    public class AsignarDocenteSesionService
    {
        private readonly ISesionRepositorio _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;
        private readonly IBloqueTiempoRepositorio _bloques;
        private readonly IDocenteRepositorio _docentes;
        private readonly IHorarioRepositorio? _horarios;
        private readonly IGrupoRepositorio? _grupos;
        private readonly IAsignaturaRepositorio? _asignaturas;

        // horarios/grupos/asignaturas son opcionales (default null → mensaje degradado sin
        // nombre, comportamiento anterior) para no romper la firma del constructor en tests
        // existentes que no los proveen.
        public AsignarDocenteSesionService(
            ISesionRepositorio sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IBloqueTiempoRepositorio bloques,
            IDocenteRepositorio docentes,
            IHorarioRepositorio? horarios = null,
            IGrupoRepositorio? grupos = null,
            IAsignaturaRepositorio? asignaturas = null)
        {
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _bloques      = bloques;
            _docentes     = docentes;
            _horarios     = horarios;
            _grupos       = grupos;
            _asignaturas  = asignaturas;
        }

        /// <exception cref="KeyNotFoundException">Sesión o docente no encontrado → 404.</exception>
        /// <exception cref="InvalidOperationException">Solape de franja duro → 409.</exception>
        public async Task<AsignarDocenteResponse> EjecutarAsync(AsignarDocenteRequest req)
        {
            var sesion = await _sesiones.GetByIdAsync(req.SesionId)
                ?? throw new KeyNotFoundException($"No se encontró la sesión con Id '{req.SesionId}'.");

            // Desasignar: path simple, sin validaciones.
            if (req.DocenteId is null)
            {
                sesion.AsignarDocente(null);
                await _sesiones.UpdateAsync(sesion);
                return new AsignarDocenteResponse { SesionId = sesion.Id.ToString() };
            }

            var docente = await _docentes.GetByIdAsync(req.DocenteId.Value)
                ?? throw new KeyNotFoundException($"No se encontró el docente con Id '{req.DocenteId}'.");

            // G4 auditoría: cada POST /horario/generar AGREGA sesiones sin borrar las de una
            // corrida anterior (GenerarHorarioService.AddRangeAsync, sin delete previo). Sin
            // acotar por horario, el solape y la carga se calculaban contra TODA la historia de
            // corridas — la carga de un docente crecía sin límite con cada regeneración. Se
            // excluyen las sesiones de horarios distintos al de esta sesión; las que no
            // pertenecen a ningún horario (p. ej. una sesión manual) siguen contando.
            var idsDeOtrosHorarios = await IdsDeOtrosHorariosAsync(sesion.Id);
            // A3 auditoría: se busca una vez y se reutiliza en el chequeo duro y el blando de abajo
            // (antes VerificarSolapeAsync hacía su propio fetch por separado).
            var bloqueDict = (await _bloques.GetAllAsync()).ToDictionary(b => b.Id);
            // PERF4 auditoría: antes _sesiones.GetAllAsync() se llamaba aquí Y otra vez dentro de
            // VerificarSolapeAsync — misma tabla completa, dos round trips por request. Se filtra
            // una vez (fuera de la historia de otros horarios, mismo criterio de ambos chequeos)
            // y se reutiliza para el duro y el blando.
            var sesionesVigentes = (await _sesiones.GetAllAsync())
                .Where(s => !idsDeOtrosHorarios.Contains(s.Id))
                .ToList();

            // HARD: solape de franja del docente en la misma semana.
            await VerificarSolapeAsync(sesion, req.DocenteId.Value, sesionesVigentes, bloqueDict);

            // SOFT: disponibilidad y carga (advertencias, no rechazo).
            var todasSesionesDocente = sesionesVigentes
                .Where(s => s.DocenteId == req.DocenteId.Value)
                .ToList();
            var advertencias = VerificarBlandas(sesion, docente, todasSesionesDocente, bloqueDict);

            sesion.AsignarDocente(req.DocenteId.Value);
            await _sesiones.UpdateAsync(sesion);

            return new AsignarDocenteResponse
            {
                SesionId     = sesion.Id.ToString(),
                DocenteId    = sesion.DocenteId?.ToString(),
                Advertencias = advertencias
            };
        }

        /// <summary>
        /// Ids de sesiones que pertenecen a un <see cref="Horario"/> distinto del de
        /// <paramref name="sesionId"/> — corridas de generación superadas por una regeneración
        /// posterior. Una sesión que no pertenece a ningún horario (p. ej. creada a mano) no
        /// entra aquí: sigue contando como carga/solape real, no se excluye.
        /// </summary>
        private async Task<HashSet<Guid>> IdsDeOtrosHorariosAsync(Guid sesionId)
        {
            if (_horarios is null) return new HashSet<Guid>();
            var horarios = await _horarios.GetAllAsync();
            var propio = horarios.FirstOrDefault(h => h.SesioneIds.Contains(sesionId));
            // Bug: `h.Id != propio?.Id` con propio=null evaluaba a `h.Id != null`, cierto para
            // TODOS los horarios (Guid no es Guid?), así que una sesión sin horario propio
            // excluía TODA la historia de corridas del chequeo — justo lo opuesto de lo que dice
            // el docstring. Sin horario propio no hay nada que excluir.
            if (propio is null) return new HashSet<Guid>();
            return horarios
                .Where(h => h.Id != propio.Id)
                .SelectMany(h => h.SesioneIds)
                .ToHashSet();
        }

        // ── Validación hard ──────────────────────────────────────────────────────

        private async Task VerificarSolapeAsync(
            Sesion sesion, Guid docenteId, List<Sesion> sesionesVigentes, Dictionary<Guid, BloqueTiempo> bloqueDict)
        {
            var otrasDocente = sesionesVigentes
                .Where(s => s.DocenteId == docenteId && s.Id != sesion.Id)
                .ToDictionary(s => s.Id);

            if (otrasDocente.Count == 0) return;

            var todosIds    = otrasDocente.Keys.Append(sesion.Id).ToList();
            var todasAsigs  = await _asignaciones.GetBySesionIdsAsync(todosIds);

            // Sin agrupar por semana: la franja de una sesión es la misma todas las semanas
            // (ALT-05), y una que alterna sigue ocupando al docente la semana en que se dicta en
            // línea. Agrupar por Semana dejaría de comparar una fila de la semana A contra una de
            // la B y perdería solapes reales, devolviendo 200 en vez de 409.
            var targetAsigs = todasAsigs.Where(a => a.SesionId == sesion.Id).ToList();
            var otrasAsigs  = todasAsigs.Where(a => a.SesionId != sesion.Id).ToList();

            foreach (var targetAsig in targetAsigs)
            {
                if (!bloqueDict.TryGetValue(targetAsig.BloqueTiempoId, out var tBloque)) continue;
                var tStart = tBloque.HoraInicio;
                var tEnd   = tStart.AddHours((double)sesion.DuracionHoras);

                foreach (var otraAsig in otrasAsigs)
                {
                    if (!bloqueDict.TryGetValue(otraAsig.BloqueTiempoId, out var oBloque)) continue;
                    if (tBloque.Dia != oBloque.Dia) continue;

                    var oSesion = otrasDocente[otraAsig.SesionId];
                    var oStart  = oBloque.HoraInicio;
                    var oEnd    = oStart.AddHours((double)oSesion.DuracionHoras);

                    if (tStart < oEnd && oStart < tEnd)
                    {
                        var d1 = await DescribirSesionAsync(sesion, tBloque.Dia, tStart, tEnd);
                        var d2 = await DescribirSesionAsync(oSesion, oBloque.Dia, oStart, oEnd);
                        throw new BusinessRuleViolationException(
                            $"HC-I01 (edición): el docente ya tiene otra sesión en esa franja. " +
                            $"Sesión 1: {d1}. Sesión 2: {d2}. " +
                            "Elija otro docente para una de las dos sesiones, o cambie el horario de una de ellas.");
                    }
                }
            }
        }

        /// <summary>"Cálculo I · G1 (lunes 08:00–10:00)" — degrada a "sesión sin nombre" si los
        /// repositorios de grupo/asignatura no fueron provistos o el Id no resuelve.</summary>
        private async Task<string> DescribirSesionAsync(Sesion s, DiaDeSemana dia, TimeOnly inicio, TimeOnly fin)
        {
            var asignatura = _asignaturas is not null
                ? (await _asignaturas.GetByIdAsync(s.AsignaturaId))?.Nombre
                : null;
            var grupo = _grupos is not null && s.GrupoId.HasValue
                ? (await _grupos.GetByIdAsync(s.GrupoId.Value))?.Nombre
                : null;

            var nombre = (asignatura, grupo) switch
            {
                (not null, not null) => $"{asignatura} · {grupo}",
                (not null, null)      => asignatura,
                _                      => "sesión sin nombre"
            };
            return $"{nombre} ({dia} {inicio:HH\\:mm}–{fin:HH\\:mm})";
        }

        // ── Validaciones blandas ─────────────────────────────────────────────────

        private static List<string> VerificarBlandas(
            Sesion sesion, Docente docente, List<Sesion> sesionesDocente, Dictionary<Guid, BloqueTiempo> bloqueDict)
        {
            var advertencias = new List<string>();

            // A3 auditoría: HC-I02 blanda comparaba solo el bloque de INICIO — un docente
            // disponible únicamente 07:00–08:00 no recibía advertencia por una sesión de 4h a las
            // 07:00 (07:00–11:00), mientras el chequeo DURO de este mismo archivo (VerificarSolapeAsync)
            // sí usa la duración completa. Ahora comprueba TODA la franja de la sesión.
            if (docente.BloquesDisponibles.Count > 0 && bloqueDict.TryGetValue(sesion.BloqueTiempoId, out var bloqueSesion))
            {
                int dur = Math.Max(1, (int)Math.Ceiling(sesion.DuracionHoras));
                bool todaLaFranjaDisponible = true;
                for (int k = 0; k < dur; k++)
                {
                    var horaK = bloqueSesion.HoraInicio.AddHours(k);
                    if (!docente.BloquesDisponibles.Any(b => b.Dia == bloqueSesion.Dia && b.HoraInicio == horaK))
                    {
                        todaLaFranjaDisponible = false;
                        break;
                    }
                }
                if (!todaLaFranjaDisponible)
                    advertencias.Add(
                        "La franja asignada cae fuera de la disponibilidad declarada del docente (HC-I02 degradada — solo advertencia).");
            }

            // HC-I03 blanda: carga semanal máxima.
            var horasYaAsignadas = sesionesDocente
                .Where(s => s.Id != sesion.Id)
                .Sum(s => s.DuracionHoras);
            var horasTotales = horasYaAsignadas + sesion.DuracionHoras;
            if (horasTotales > docente.MaximoHorasSemanales)
                advertencias.Add(
                    $"La asignación supera el máximo de horas semanales del docente " +
                    $"({horasTotales:0.#}/{docente.MaximoHorasSemanales:0.#} h) — solo advertencia.");

            return advertencias;
        }
    }
}
