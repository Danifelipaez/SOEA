using System;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IAsignaturaRepositorio : IRepositorio<Asignatura>
    {
        Task<Asignatura?> GetByCodigoAsync(string codigo);
        Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId);
        Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId);
    }
}
