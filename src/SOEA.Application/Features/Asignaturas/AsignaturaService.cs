using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.Application.Features.Asignaturas;

public class AsignaturaService
{
    private readonly IAsignaturaRepositorio _repository;

    public AsignaturaService(IAsignaturaRepositorio repository) => _repository = repository;

    public async Task<AsignaturaResponse> CreateAsync(CreateAsignaturaRequest request)
    {
        var asignatura = new Asignatura(
            Guid.NewGuid(),
            request.Nombre,
            request.Codigo,
            request.HorasPorSesion,
            request.SesionesPorSemana,
            request.SesionesLaboratorioSemestre,
            request.ProgramaId);

        await _repository.AddAsync(asignatura);
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task<AsignaturaResponse> GetByIdAsync(Guid id)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task<List<AsignaturaResponse>> GetAllAsync()
    {
        var asignaturas = await _repository.GetAllAsync();
        return asignaturas.Select(AsignaturaResponse.FromEntity).ToList();
    }

    public async Task<AsignaturaResponse> UpdateAsync(Guid id, UpdateAsignaturaRequest request)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");

        asignatura.ActualizarDatos(
            request.Nombre,
            request.Codigo,
            request.HorasPorSesion,
            request.SesionesPorSemana,
            request.SesionesLaboratorioSemestre,
            request.ProgramaId,
            request.Alternancia,
            categoria: request.Categoria);
        asignatura.AsignarDocente(request.DocenteId);
        asignatura.AsignarEspacioFijo(request.EspacioFijoId);

        await _repository.UpdateAsync(asignatura);
        return AsignaturaResponse.FromEntity(asignatura);
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _repository.GetByIdAsync(id) is null)
            throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");
        await _repository.DeleteAsync(id);
    }

    /// <summary>
    /// Permite a la coordinadora asignar manualmente el tipo de alternancia
    /// (TipoA / TipoB / SinAlternancia) a una asignatura existente.
    /// </summary>
    public async Task UpdateAlternanciaAsync(Guid id, TipoAlternancia alternancia)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");
        asignatura.EstablecerAlternancia(alternancia);
        await _repository.UpdateAsync(asignatura);
    }
}
