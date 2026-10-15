using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
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

    /// <summary>Dos espacios con el mismo nombre son indistinguibles en el horario y en la importación por nombre.</summary>
    public async Task ExigirNombreUnicoAsync(string nombre, Guid id)
    {
        var otro = await _repo.GetByNombreAsync((nombre ?? "").Trim());
        if (otro is not null && otro.Id != id)
            throw new BusinessRuleViolationException($"Ya existe un espacio llamado '{otro.Nombre}'.");
    }

    public Task<List<Espacio>> ListarAsync() => _repo.GetAllAsync();

    public async Task<Espacio> CrearAsync(Guid id, string nombre, TipoEspacio tipo, int capacidad, string? edificio, int? piso)
    {
        if (id == Guid.Empty) id = Guid.NewGuid();
        await ExigirNombreUnicoAsync(nombre, id);
        var espacio = new Espacio(id, nombre, tipo, capacidad, edificio, piso);
        await _repo.AddAsync(espacio);
        return espacio;
    }

    public async Task<Espacio> ActualizarAsync(Guid id, string nombre, TipoEspacio tipo, int capacidad, string? edificio, int? piso)
    {
        var existing = await _repo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Espacio con ID {id} no encontrado.");
        await ExigirNombreUnicoAsync(nombre, id);
        existing.ActualizarDatos(nombre, tipo, edificio, piso);
        existing.ActualizarCapacidad(capacidad);
        await _repo.UpdateAsync(existing);
        return existing;
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
