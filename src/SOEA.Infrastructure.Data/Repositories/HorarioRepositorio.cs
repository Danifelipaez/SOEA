using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;
using System.Threading.Tasks;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class HorarioRepositorio : BaseRepository<Horario>, IHorarioRepositorio
    {
        public HorarioRepositorio(SOEABdContext context) : base(context)
        {
        }

        public async Task<Horario?> GetBySemestreAsync(string semestre)
        {
            return await _dbSet
                .FirstOrDefaultAsync(h => h.Semestre == semestre);
        }
    }
}
