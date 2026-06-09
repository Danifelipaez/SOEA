using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IDocenteRepositorio : IRepositorio<Docente>
    {
        Task<Docente?> GetByCedulaAsync(string cedula);
        Task<Docente?> GetByNombreAsync(string nombre);
    }
}
