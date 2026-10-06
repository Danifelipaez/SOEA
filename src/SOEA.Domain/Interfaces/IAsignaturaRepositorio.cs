using System;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IAsignaturaRepositorio : IRepositorio<Asignatura>
    {
        Task<Asignatura?> GetByCodigoAsync(string codigo);
        Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId);
        Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId);

        /// <summary>Asignaturas de un programa (guard de borrado). Por defecto filtra en memoria; el
        /// repositorio EF lo resuelve con un COUNT en la BD.</summary>
        async Task<int> ContarPorProgramaAsync(Guid programaId)
            => (await GetAllAsync()).Count(a => a.ProgramaId == programaId);
    }
}
