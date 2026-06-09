using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IFacultadRepositorio : IRepositorio<Facultad>
    {
        Task<Facultad?> GetByNombreAsync(string nombre);
    }
}
