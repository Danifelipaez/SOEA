using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class DocenteRepositorio : BaseRepository<Docente>, IDocenteRepositorio
    {
        public DocenteRepositorio(SOEABdContext context) : base(context) { }

        public async Task<Docente?> GetByCedulaAsync(string cedula)
            => await _dbSet.FirstOrDefaultAsync(d => d.CedulaIdentidad == cedula);
    }
}
