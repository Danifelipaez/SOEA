using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class BloqueTiempoRepositorio : BaseRepository<BloqueTiempo>, IBloqueTiempoRepositorio
    {
        public BloqueTiempoRepositorio(SOEABdContext context) : base(context) { }

        public async Task<BloqueTiempo?> FindByDiaHoraAsync(DiaDeSemana dia, TimeOnly horaInicio)
            => await _dbSet.FirstOrDefaultAsync(b => b.Dia == dia && b.HoraInicio == horaInicio);

        public async Task<bool> ExisteAlgunoAsync()
            => await _dbSet.AnyAsync();
    }
}
