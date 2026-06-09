using System;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IGrupoRepositorio : IRepositorio<Grupo>
    {
        Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId);
    }
}
