using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Repositorio de Asignatura. Hereda CRUD base de IRepository.
    /// Agregar aquí solo métodos específicos de Asignatura (ej: GetByCodigoAsync).
    /// </summary>
    public interface IAsignaturaRepositorio : IRepositorio<Asignatura>
    {
        Task<Asignatura?> GetByCodigoAsync(string codigo);
    }
}
