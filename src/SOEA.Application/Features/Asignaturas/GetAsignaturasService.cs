using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.Application.Features.Asignaturas
{
    public class GetAsignaturasService
    {
        private readonly IAsignaturaRepositorio _repository;

        public GetAsignaturasService(IAsignaturaRepositorio repository)
        {
            _repository = repository;
        }

        public async Task<List<AsignaturaResponse>> ExecuteAsync()
        {
            var asignaturas = await _repository.GetAllAsync();
            return asignaturas.Select(MapToResponse).ToList();
        }

        private static AsignaturaResponse MapToResponse(SOEA.Domain.Entities.Asignatura asignatura)
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
}
