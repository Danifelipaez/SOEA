using SOEA.Application.Interfaces;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
namespace SOEA.Application.Features.Asignaturas;

public class CreateAsignaturaRequest
{
    public string Nombre { get; set; }="";
    public string Codigo { get; set; }="";
    public int BloquesSemanales { get; set; }
    public bool RequiereLab { get; set; }
    public TipoAlternancia Alternancia { get; set; }
    public Guid ProgramaId { get; set; }

}

public class AsignaturaResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; }="";
    public string Codigo { get; set; }="";
    public int BloquesSemanales { get; set; }
    public bool RequiereLab { get; set; }
    public TipoAlternancia Alternancia { get; set; }
    public Guid ProgramaId { get; set; }
    
}
public class CreateAsignaturaService
{
    private readonly IAsignaturaRepository _repository;
    public CreateAsignaturaService(IAsignaturaRepository repository)
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