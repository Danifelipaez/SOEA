using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Sesiones;

/// <summary>
/// Purga en cascada las Sesiones generadas (y sus AsignacionSemanal) que referencian una entidad
/// de catálogo antes de borrarla — Sesion.AsignaturaId/GrupoId/EspacioId son FK Restrict (M14
/// auditoría), así que sin esto el borrado de catálogo falla con un 409 genérico o queda bloqueado.
/// Las sesiones son datos regenerables de una corrida, no catálogo: mismo principio que ya aplica
/// EspacioService/DocenteService para relaciones sin FK real.
/// </summary>
public class SesionCascadeService
{
    private readonly ISesionRepositorio _sesiones;
    private readonly IAsignacionSemanalRepositorio _asignaciones;

    public SesionCascadeService(ISesionRepositorio sesiones, IAsignacionSemanalRepositorio asignaciones)
    {
        _sesiones = sesiones;
        _asignaciones = asignaciones;
    }

    public Task EliminarPorGrupoAsync(Guid grupoId) => EliminarAsync(_sesiones.GetIdsByGrupoIdAsync(grupoId));

    public Task EliminarPorAsignaturaAsync(Guid asignaturaId) => EliminarAsync(_sesiones.GetIdsByAsignaturaIdAsync(asignaturaId));

    public Task EliminarPorEspacioAsync(Guid espacioId) => EliminarAsync(_sesiones.GetIdsByEspacioIdAsync(espacioId));

    private async Task EliminarAsync(Task<List<Guid>> idsAsync)
    {
        var ids = await idsAsync;
        if (ids.Count == 0) return;

        // Orden importa: AsignacionSemanal.SesionId no tiene FK propia (mismo patrón que la
        // limpieza de corridas anteriores en GenerarHorarioService), así que se borra primero
        // para no dejar asignaciones huérfanas.
        await _asignaciones.DeleteBySesionIdsAsync(ids);
        await _sesiones.DeleteRangeAsync(ids);
    }
}
