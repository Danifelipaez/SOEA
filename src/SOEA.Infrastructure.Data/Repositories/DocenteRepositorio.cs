using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class DocenteRepositorio : BaseRepository<Docente>, IDocenteRepositorio
    {
        public DocenteRepositorio(SOEABdContext context) : base(context) { }

        public override async Task<List<Docente>> GetAllAsync()
            => await _dbSet.Include(d => d.BloquesDisponibles).ToListAsync();

        public async Task<Docente?> GetByCedulaAsync(string cedula)
            => await _dbSet.FirstOrDefaultAsync(d => d.CedulaIdentidad == cedula);
    }
}
