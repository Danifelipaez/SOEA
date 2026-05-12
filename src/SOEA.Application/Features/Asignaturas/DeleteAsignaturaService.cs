using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Asignaturas
{
    public class DeleteAsignaturaService
    {
        private readonly IAsignaturaRepositorio _repository;

        public DeleteAsignaturaService(IAsignaturaRepositorio repository)
        {
            _repository = repository;
        }

        public async Task ExecuteAsync(Guid id)
        {
            var asignatura = await _repository.GetByIdAsync(id);
            
            if (asignatura == null)
                throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");

            await _repository.DeleteAsync(id);
        }
    }
}
