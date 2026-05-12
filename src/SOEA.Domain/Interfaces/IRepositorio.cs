using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Contrato base para todos los repositorios del dominio.
    /// Cada entidad implementa este contrato y puede extenderlo con métodos específicos.
    /// </summary>
    public interface IRepositorio<T> where T : EntidadBase
    {
        Task AddAsync(T entity);
        Task<T?> GetByIdAsync(Guid id);
        Task<List<T>> GetAllAsync();
        Task UpdateAsync(T entity);
        Task DeleteAsync(Guid id);
    }
}
