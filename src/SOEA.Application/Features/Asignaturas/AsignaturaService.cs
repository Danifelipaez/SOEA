using SOEA.Domain.Entities;
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
            request.Id == Guid.Empty ? Guid.NewGuid() : request.Id,
            request.Nombre,
            request.Codigo,
            sesionesTeoriaPresencialSemana: request.SesionesTeoriaPresencialSemana,
            horasTeoriaPresencial: request.HorasTeoriaPresencial,
            sesionesTeoriaVirtualSemana: request.SesionesTeoriaVirtualSemana,
            horasTeoriaVirtual: request.HorasTeoriaVirtual,
            sesionesLaboratorioSemana: request.SesionesLaboratorioSemana,
            horasLaboratorio: request.HorasLaboratorio,
            sesionesLaboratorioSemestre: request.SesionesLaboratorioSemestre,
            programaId: request.ProgramaId,
            categoria: request.Categoria ?? Domain.Enums.CategoriaAsignatura.Obligatoria,
            horaInicioMin: ParseHora(request.HoraInicioMin),
            horaFinMax: ParseHora(request.HoraFinMax));

        if (request.Alternancia.HasValue)
            asignatura.EstablecerAlternancia(request.Alternancia.Value);

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
            sesionesTeoriaPresencialSemana: request.SesionesTeoriaPresencialSemana,
            horasTeoriaPresencial: request.HorasTeoriaPresencial,
            sesionesTeoriaVirtualSemana: request.SesionesTeoriaVirtualSemana,
            horasTeoriaVirtual: request.HorasTeoriaVirtual,
            sesionesLaboratorioSemana: request.SesionesLaboratorioSemana,
            horasLaboratorio: request.HorasLaboratorio,
            sesionesLaboratorioSemestre: request.SesionesLaboratorioSemestre,
            programaId: request.ProgramaId,
            alternanciaExplicita: request.Alternancia,
            categoria: request.Categoria,
            horaInicioMin: ParseHora(request.HoraInicioMin),
            horaFinMax: ParseHora(request.HoraFinMax));

        await _repository.UpdateAsync(asignatura);
        return AsignaturaResponse.FromEntity(asignatura);
    }

    // Mismo criterio que GenerarHorarioService.ParseHora — TimeOnly.TryParse acepta "HH:mm".
    private static TimeOnly? ParseHora(string? hhmm) =>
        !string.IsNullOrWhiteSpace(hhmm) && TimeOnly.TryParse(hhmm, out var t) ? t : null;

    public async Task DeleteAsync(Guid id)
    {
        if (await _repository.GetByIdAsync(id) is null)
            throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");
        await _repository.DeleteAsync(id);
    }

    /// <summary>
    /// Marca (o desmarca) la asignatura como candidata a ceder a alternancia si el algoritmo
    /// agota el espacio físico disponible (cesión por saturación de espacio).
    /// </summary>
    public async Task UpdateElegibilidadAlternanciaAsync(Guid id, bool elegible)
    {
        var asignatura = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");
        asignatura.EstablecerElegibilidadAlternancia(elegible);
        await _repository.UpdateAsync(asignatura);
    }
}
