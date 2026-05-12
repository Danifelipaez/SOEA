using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas.Responses;

namespace SOEA.Application.Features.Asignaturas
{
    public class GetAsignaturaByIdService
    {
        private readonly IAsignaturaRepositorio _repository;

        public GetAsignaturaByIdService(IAsignaturaRepositorio repository)
        {
            _repository = repository;
        }

        public async Task<AsignaturaResponse> ExecuteAsync(Guid id)
        {
            var asignatura = await _repository.GetByIdAsync(id);
            
            if (asignatura == null)
                throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");

            return MapToResponse(asignatura);
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
