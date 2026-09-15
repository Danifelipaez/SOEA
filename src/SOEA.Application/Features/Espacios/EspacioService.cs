using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Sesiones;

namespace SOEA.Application.Features.Espacios;

/// <summary>
/// Borrado de Espacio con guard: Espacio.Id no tiene FK en BD desde Grupo.RequisitosEspacio
/// (columna JSON), así que sin este guard el borrado dejaba grupos exigiendo un espacio que ya
/// no existe. Mismo patrón que AsignaturaService.DeleteAsync (Asignatura→Grupo) y
/// DocenteService.DeleteAsync (Docente→Grupo). Sesion.EspacioId sí es FK Restrict (M14 auditoría)
/// — el comentario original decía "no revisa Sesiones a propósito" asumiendo que no bloqueaban,
/// pero con esa FK real un espacio con sesiones generadas fallaba igual con un 409 genérico.
/// Se purgan en cascada por el mismo motivo que Asignatura/Grupo: son datos regenerables, no
/// catálogo.
/// </summary>
public class EspacioService
{
    private readonly IEspacioRepositorio _repo;
    private readonly IGrupoRepositorio _grupoRepo;
    private readonly SesionCascadeService _sesionCascade;
    private readonly IUnitOfWork _uow;

    public EspacioService(IEspacioRepositorio repo, IGrupoRepositorio grupoRepo, SesionCascadeService sesionCascade, IUnitOfWork uow)
    {
        _repo = repo;
        _grupoRepo = grupoRepo;
        _sesionCascade = sesionCascade;
        _uow = uow;
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repo.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Espacio con ID {id} no encontrado.");

        var grupos = await _grupoRepo.GetAllAsync();
        var cantidad = grupos.Count(g => g.RequisitosEspacio.Any(r => r.EspacioId == id));
        if (cantidad > 0)
            throw new BusinessRuleViolationException(
                $"No se puede eliminar el espacio: {cantidad} grupo(s) lo exigen como requisito de espacio. Actualice esos grupos primero.");

        await _uow.BeginTransactionAsync();
        try
        {
            await _sesionCascade.EliminarPorEspacioAsync(id);
            await _repo.DeleteAsync(id);
            await _uow.CommitAsync();
        }
        catch
        {
            await _uow.RollbackAsync();
            throw;
        }
    }
}
