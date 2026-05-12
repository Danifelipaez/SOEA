using SOEA.Domain.Interfaces;
using SOEA.Domain.Entities;
using SOEA.Application.Features.Asignaturas.Requests;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.Application.Features.Asignaturas;
public class CreateAsignaturaService
{
    private readonly IAsignaturaRepositorio _repository;
    public CreateAsignaturaService(IAsignaturaRepositorio repository)
    {
        _repository = repository;
    }

    public async Task<AsignaturaResponse> ExecuteAsync(CreateAsignaturaRequest request)
    {
        var newAsignatura = new Asignatura(
            Guid.NewGuid(),
            request.Nombre,
            request.Codigo,
            request.BloquesSemanales,
            request.RequiereLab,
            request.Alternancia,
            request.ProgramaId
        );

        await _repository.AddAsync(newAsignatura);
        return MapToResponse(newAsignatura);
    }

    private static AsignaturaResponse MapToResponse(Asignatura asignatura)
    {
        return new AsignaturaResponse
        {
            Id = asignatura.Id,
            Nombre = asignatura.Nombre,
            Codigo = asignatura.Codigo,
            BloquesSemanales = asignatura.BloquesSemanales,
            RequiereLab = asignatura.RequiereLab,
            Alternancia = asignatura.Alternancia,
            ProgramaId = asignatura.ProgramaId
        };
    }
}
