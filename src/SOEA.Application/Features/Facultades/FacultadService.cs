using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Facultades;

/// <summary>
/// Borrado de Facultad con guard (DB-6 auditoría 2026-09-28): antes DELETE /facultades/{id} respondía
/// 204 aunque tuviera programas, y estos quedaban apuntando a una facultad inexistente. Programas.facultad_id
/// ya es FK Restrict; este guard evita que el usuario vea un 409 genérico. Mismo patrón que EspacioService.
/// </summary>
public class FacultadService
{
    private readonly IFacultadRepositorio _repo;
    private readonly IProgramaRepositorio _programaRepo;

    public FacultadService(IFacultadRepositorio repo, IProgramaRepositorio programaRepo)
    {
        _repo = repo;
        _programaRepo = programaRepo;
    }

    /// <summary>Dos facultades con el mismo nombre confunden al usuario y a la importación por nombre.</summary>
    public async Task ExigirNombreUnicoAsync(string nombre, Guid id)
    {
        var otra = await _repo.GetByNombreAsync((nombre ?? "").Trim());
        if (otra is not null && otra.Id != id)
            throw new BusinessRuleViolationException($"Ya existe una facultad llamada '{otra.Nombre}'.");
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repo.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Facultad con ID {id} no encontrada.");

        var programas = await _programaRepo.ContarPorFacultadAsync(id);
        if (programas > 0)
            throw new BusinessRuleViolationException(
                $"No se puede eliminar la facultad: tiene {programas} programa(s). Elimínelos o cámbielos de facultad primero.");

        await _repo.DeleteAsync(id);
    }
}
