using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class SesionRepositorio : BaseRepository<Sesion>, ISesionRepositorio
    {
        public SesionRepositorio(SOEABdContext context) : base(context) { }

        public async Task AddRangeAsync(IEnumerable<Sesion> sesiones)
        {
            await _dbSet.AddRangeAsync(sesiones);
            await _context.SaveChangesAsync();
        }
    }
}
