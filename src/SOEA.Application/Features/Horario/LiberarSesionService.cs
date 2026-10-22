using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// "Liberar espacio del horario": saca una clase del horario vigente para dejar libre su franja y
    /// su aula. La clase no se guarda aparte como pendiente: el frontend la deduce comparando lo que
    /// el catálogo pide para cada grupo con lo que hay en el horario, y la próxima generación la vuelve
    /// a crear. Una clase con pareja de alternancia se libera junto con su pareja: sola, la otra
    /// seguiría alternando sin nadie que use su aula la semana contraria (regla 10).
    /// </summary>
    public class LiberarSesionService
    {
        private readonly IHorarioRepositorio           _horarios;
        private readonly ISesionRepositorio            _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;
        private readonly IUnitOfWork                   _uow;

        public LiberarSesionService(
            IHorarioRepositorio           horarios,
            ISesionRepositorio            sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IUnitOfWork                   uow)
        {
            _horarios     = horarios;
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _uow          = uow;
        }

        /// <returns>Ids de las clases liberadas (la pedida y, si la tiene, su pareja).</returns>
        public async Task<List<Guid>> EjecutarAsync(Guid horarioId, Guid sesionId)
        {
            var horario = await _horarios.GetByIdAsync(horarioId)
                ?? throw new KeyNotFoundException("El horario cambió mientras trabajaba. Recargue la página.");
            if (!horario.SesioneIds.Contains(sesionId))
                throw new KeyNotFoundException("Esa clase ya no está en el horario. Recargue la página.");
            if (horario.Estado == EstadoHorario.Publicado)
                throw new BusinessRuleViolationException("El horario ya está publicado: no se pueden sacar clases.");

            var sesiones = await _sesiones.GetByIdsAsync(horario.SesioneIds);
            var sesion = sesiones.FirstOrDefault(s => s.Id == sesionId)
                ?? throw new KeyNotFoundException("Esa clase ya no está en el horario. Recargue la página.");
            var ids = sesion.ParejaAlternanciaId is Guid pareja
                ? sesiones.Where(s => s.ParejaAlternanciaId == pareja).Select(s => s.Id).ToList()
                : new List<Guid> { sesionId };
            // Un horario sin clases se lee como "no hay horario" (ObtenerActualAsync, por cómo trabaja
            // Limpiar): tras recargar se perderían también las pendientes. Vaciarlo es Limpiar.
            if (ids.Count >= sesiones.Count)
                throw new BusinessRuleViolationException(
                    "Son las últimas clases del horario. Para empezar de cero use «Limpiar horario».");

            await _uow.BeginTransactionAsync();
            try
            {
                // Mismo orden que LimpiarAsync: AsignacionSemanal.SesionId no tiene FK.
                await _asignaciones.DeleteBySesionIdsAsync(ids);
                await _sesiones.DeleteRangeAsync(ids);
                horario.QuitarSesiones(ids);
                await _horarios.UpdateAsync(horario);
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }
            return ids;
        }
    }
}
