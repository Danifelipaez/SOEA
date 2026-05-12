using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class AsignaturaRepository : BaseRepository<Asignatura>, IAsignaturaRepositorio
    {
        public AsignaturaRepository(SOEABdContext context) : base(context) { }

        public async Task<Asignatura?> GetByCodigoAsync(string codigo)
            => await _dbSet.FirstOrDefaultAsync(a => a.Codigo == codigo);
    }
}
