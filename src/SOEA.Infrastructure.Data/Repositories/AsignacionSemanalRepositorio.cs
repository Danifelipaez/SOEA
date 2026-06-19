using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class AsignacionSemanalRepositorio : BaseRepository<AsignacionSemanal>, IAsignacionSemanalRepositorio
    {
        public AsignacionSemanalRepositorio(SOEABdContext context) : base(context) { }

        public async Task AddRangeAsync(IEnumerable<AsignacionSemanal> asignaciones)
        {
            await _dbSet.AddRangeAsync(asignaciones);
            await _context.SaveChangesAsync();
        }

        public async Task<List<AsignacionSemanal>> GetBySesionIdsAsync(IEnumerable<Guid> sesionIds)
        {
            var ids = sesionIds.ToList();
            return await _dbSet
                .AsNoTracking()
                .Where(a => ids.Contains(a.SesionId))
                .ToListAsync();
        }
    }
}
