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
            // Un semestre acumula una fila por corrida (G4 auditoría: nunca se borran), así que sin
            // ordenar esto devolvía una fila arbitraria (orden físico de la tabla) en vez de la
            // corrida vigente — la última generada es la única con sesiones vivas en BD.
            return await _dbSet
                .Where(h => h.Semestre == semestre)
                .OrderByDescending(h => h.GeneradoEn)
                .FirstOrDefaultAsync();
        }
    }
}
