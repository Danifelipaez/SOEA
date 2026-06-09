using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    public interface IEspacioRepositorio : IRepositorio<Espacio>
    {
        Task<Espacio?> GetByNombreAsync(string nombre);
    }
}
