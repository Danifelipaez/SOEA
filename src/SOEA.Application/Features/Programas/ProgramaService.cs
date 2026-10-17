using SOEA.Domain.Entities;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Programas;

/// <summary>
/// Borrado de Programa con guard (DB-6 auditoría 2026-09-28): antes se borraba sin mirar sus asignaturas
/// (sin FK, quedaban apuntando a un programa inexistente). Los grupos cuelgan de las asignaturas
/// (Grupo → Asignatura → Programa), así que basta con mirar estas. Mismo patrón que EspacioService/FacultadService.
/// </summary>
public class ProgramaService
{
    private readonly IProgramaRepositorio _repo;
    private readonly IAsignaturaRepositorio _asignaturaRepo;

    public ProgramaService(IProgramaRepositorio repo, IAsignaturaRepositorio asignaturaRepo)
    {
        _repo = repo;
        _asignaturaRepo = asignaturaRepo;
    }

    /// <summary>Un programa repetido (mismo nombre y facultad) hacía fallar TODA importación de Excel.</summary>
    public async Task ExigirNombreUnicoAsync(string nombre, Guid facultadId, Guid id)
    {
        var otro = await _repo.GetByNombreYFacultadAsync((nombre ?? "").Trim(), facultadId);
        if (otro is not null && otro.Id != id)
            throw new BusinessRuleViolationException($"Ya existe un programa llamado '{otro.Nombre}' en esa facultad.");
    }

    public async Task<List<Programa>> ListarAsync() => (await _repo.GetAllAsync()).OrderBy(p => p.Nombre).ToList();

    public async Task<Programa> CrearAsync(Guid id, string nombre, Guid facultadId)
    {
        if (id == Guid.Empty) id = Guid.NewGuid();
        await ExigirNombreUnicoAsync(nombre, facultadId, id);
        var programa = new Programa(id, nombre, facultadId);
        await _repo.AddAsync(programa);
        return programa;
    }

    public async Task<Programa> ActualizarAsync(Guid id, string nombre, Guid facultadId)
    {
        var existing = await _repo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Programa con ID {id} no encontrado.");
        await ExigirNombreUnicoAsync(nombre, facultadId, id);
        existing.ActualizarDatos(nombre, facultadId);
        await _repo.UpdateAsync(existing);
        return existing;
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repo.GetByIdAsync(id) is null)
            throw new KeyNotFoundException($"Programa con ID {id} no encontrado.");

        var asignaturas = await _asignaturaRepo.ContarPorProgramaAsync(id);
        if (asignaturas > 0)
            throw new BusinessRuleViolationException(
                $"No se puede eliminar el programa: tiene {asignaturas} asignatura(s). Elimínelas o cámbielas de programa primero.");

        await _repo.DeleteAsync(id);
    }
}
