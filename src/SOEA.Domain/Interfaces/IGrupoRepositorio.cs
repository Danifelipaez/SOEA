using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IGrupoRepositorio : IRepositorio<Grupo>
    {
        Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId);
        Task<Grupo?> GetByCodigoAsync(string codigo);
        Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId);
    }
}
