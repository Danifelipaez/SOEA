using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Asignaturas
{
    public class DeleteAsignaturaService
    {
        private readonly IAsignaturaRepository _repository;

        public DeleteAsignaturaService(IAsignaturaRepository repository)
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
