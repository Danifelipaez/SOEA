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

        public AsignarDocenteSesionService(
            ISesionRepositorio sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IBloqueTiempoRepositorio bloques,
            IDocenteRepositorio docentes)
        {
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _bloques      = bloques;
            _docentes     = docentes;
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

            // HARD: solape de franja del docente en la misma semana.
            await VerificarSolapeAsync(sesion, req.DocenteId.Value);

            // SOFT: disponibilidad y carga (advertencias, no rechazo).
            var todasSesionesDocente = (await _sesiones.GetAllAsync())
                .Where(s => s.DocenteId == req.DocenteId.Value)
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

        // ── Validación hard ──────────────────────────────────────────────────────

        private async Task VerificarSolapeAsync(Sesion sesion, Guid docenteId)
        {
            var todasSesiones = await _sesiones.GetAllAsync();
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
                        throw new InvalidOperationException(
                            $"HC-I01 (edición): El docente ya tiene otra sesión que se solapa en esa franja " +
                            $"(semana {semana}: {tBloque.Dia} {tStart:HH\\:mm}–{tEnd:HH\\:mm}).");
                }
            }
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
