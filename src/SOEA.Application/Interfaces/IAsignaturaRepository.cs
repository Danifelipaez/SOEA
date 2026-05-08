using SOEA.Domain.Entities;

namespace SOEA.Application.Interfaces
{
    public interface IAsignaturaRepository
    {
        //Agrera new Asignatura a la DB
        Task AddAsync(Asignatura asignatura);
        //Obtiene una Asignatura por su ID
        Task<Asignatura?> GetByIdAsync(Guid id);
        //Obtiene todas las Asignaturas
        Task<List<Asignatura>> GetAllAsync();
        //Actualiza una Asignatura existente(fuera de scope para piloto)
        //Task UpdateAsync(Asignatura asignatura);
    }
}