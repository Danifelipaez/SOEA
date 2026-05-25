using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Interfaces
{
    public interface IBloqueTiempoRepositorio : IRepositorio<BloqueTiempo>
    {
        Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio);
        Task<bool> ExisteAlgunoAsync();
    }
}
