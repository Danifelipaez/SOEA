using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Repositorio de Asignatura. Hereda CRUD base de IRepository.
    /// Agregar aquí solo métodos específicos de Asignatura (ej: GetByCodigoAsync).
    /// </summary>
    public interface IAsignaturaRepository : IRepository<Asignatura>
    {
        Task<Asignatura?> GetByCodigoAsync(string codigo);
    }
}
