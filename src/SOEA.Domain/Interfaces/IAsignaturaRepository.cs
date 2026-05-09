using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IAsignaturaRepository
    {
        // Agrega nueva Asignatura a la DB
        Task AddAsync(Asignatura asignatura);
        
        // Obtiene una Asignatura por su ID
        Task<Asignatura?> GetByIdAsync(Guid id);
        
        // Obtiene todas las Asignaturas
        Task<List<Asignatura>> GetAllAsync();
        
        // Actualiza una Asignatura existente
        Task UpdateAsync(Asignatura asignatura);
        
        // Elimina una Asignatura por su ID
        Task DeleteAsync(Guid id);
    }
}
