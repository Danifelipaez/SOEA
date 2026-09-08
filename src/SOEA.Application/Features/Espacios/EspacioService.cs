using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Espacios;

/// <summary>
/// Borrado de Espacio con guard: Espacio.Id no tiene FK en BD desde Grupo.RequisitosEspacio
/// (columna JSON), así que sin este guard el borrado dejaba grupos exigiendo un espacio que ya
/// no existe. Mismo patrón que AsignaturaService.DeleteAsync (Asignatura→Grupo) y
/// DocenteService.DeleteAsync (Docente→Grupo). No revisa Sesiones: son datos generados y
/// transitorios (se regeneran en cada corrida), no catálogo.
/// </summary>
public class EspacioService
{
    private readonly IEspacioRepositorio _repo;
    private readonly IGrupoRepositorio _grupoRepo;

    public EspacioService(IEspacioRepositorio repo, IGrupoRepositorio grupoRepo)
    {
        _repo = repo;
        _grupoRepo = grupoRepo;
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repo.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Espacio con ID {id} no encontrado.");

        var grupos = await _grupoRepo.GetAllAsync();
        var cantidad = grupos.Count(g => g.RequisitosEspacio.Any(r => r.EspacioId == id));
        if (cantidad > 0)
            throw new InvalidOperationException(
                $"No se puede eliminar el espacio: {cantidad} grupo(s) lo exigen como requisito de espacio. Actualice esos grupos primero.");

        await _repo.DeleteAsync(id);
    }
}
