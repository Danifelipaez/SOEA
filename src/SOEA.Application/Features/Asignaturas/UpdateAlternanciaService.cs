using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Asignaturas
{
    /// <summary>
    /// Permite a la coordinadora asignar manualmente el tipo de alternancia
    /// (TipoA / TipoB / SinAlternancia) a una asignatura existente.
    /// </summary>
    public class UpdateAlternanciaService
    {
        private readonly IAsignaturaRepositorio _repository;

        public UpdateAlternanciaService(IAsignaturaRepositorio repository)
        {
            _repository = repository;
        }

        public async Task ExecuteAsync(Guid id, TipoAlternancia alternancia)
        {
            var asignatura = await _repository.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"Asignatura con ID {id} no encontrada.");

            asignatura.EstablecerAlternancia(alternancia);
            await _repository.UpdateAsync(asignatura);
        }
    }
}
