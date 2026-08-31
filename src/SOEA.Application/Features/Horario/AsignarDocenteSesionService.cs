using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
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

            // HARD: solape de franja del docente en la misma semana.
            await VerificarSolapeAsync(sesion, req.DocenteId.Value, idsDeOtrosHorarios);

            // SOFT: disponibilidad y carga (advertencias, no rechazo).
            var todasSesionesDocente = (await _sesiones.GetAllAsync())
                .Where(s => s.DocenteId == req.DocenteId.Value && !idsDeOtrosHorarios.Contains(s.Id))
                .ToList();
            var advertencias = VerificarBlandas(sesion, docente, todasSesionesDocente);

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
            return horarios
                .Where(h => h.Id != propio?.Id)
                .SelectMany(h => h.SesioneIds)
                .ToHashSet();
        }

        // ── Validación hard ──────────────────────────────────────────────────────

        private async Task VerificarSolapeAsync(Sesion sesion, Guid docenteId, HashSet<Guid> idsDeOtrosHorarios)
        {
            var todasSesiones = (await _sesiones.GetAllAsync())
                .Where(s => !idsDeOtrosHorarios.Contains(s.Id))
                .ToList();
            var otrasDocente  = todasSesiones
                .Where(s => s.DocenteId == docenteId && s.Id != sesion.Id)
                .ToDictionary(s => s.Id);

            if (otrasDocente.Count == 0) return;

            var todosIds    = otrasDocente.Keys.Append(sesion.Id).ToList();
            var todasAsigs  = await _asignaciones.GetBySesionIdsAsync(todosIds);
            var bloqueDict  = (await _bloques.GetAllAsync()).ToDictionary(b => b.Id);

            var targetAsigs = todasAsigs
                .Where(a => a.SesionId == sesion.Id)
                .ToDictionary(a => a.Semana);

            var otrasAsigs  = todasAsigs
                .Where(a => a.SesionId != sesion.Id)
                .GroupBy(a => a.Semana)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (semana, targetAsig) in targetAsigs)
            {
                if (!bloqueDict.TryGetValue(targetAsig.BloqueTiempoId, out var tBloque)) continue;
                var tStart = tBloque.HoraInicio;
                var tEnd   = tStart.AddHours((double)sesion.DuracionHoras);

                if (!otrasAsigs.TryGetValue(semana, out var otrasEnSemana)) continue;

                foreach (var otraAsig in otrasEnSemana)
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
                        throw new InvalidOperationException(
                            $"HC-I01 (edición): el docente ya tiene otra sesión en esa franja (semana {semana}). " +
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
            Sesion sesion, Docente docente, List<Sesion> sesionesDocente)
        {
            var advertencias = new List<string>();

            // HC-I02 blanda: disponibilidad por bloque (si está poblada).
            if (docente.BloquesDisponibles.Count > 0)
            {
                bool enDisponibilidad = docente.BloquesDisponibles
                    .Any(b => b.Id == sesion.BloqueTiempoId);
                if (!enDisponibilidad)
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
