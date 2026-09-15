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
    public class SesionRepositorio : BaseRepository<Sesion>, ISesionRepositorio
    {
        public SesionRepositorio(SOEABdContext context) : base(context) { }

        public async Task AddRangeAsync(IEnumerable<Sesion> sesiones)
        {
            await _dbSet.AddRangeAsync(sesiones);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> ExisteAsync(Guid asignaturaId, Guid? docenteId, Guid bloqueTiempoId)
            => await _dbSet.AnyAsync(x =>
                x.AsignaturaId   == asignaturaId  &&
                x.DocenteId      == docenteId     &&
                x.BloqueTiempoId == bloqueTiempoId);

        public async Task DeleteRangeAsync(IEnumerable<Guid> ids)
        {
            var idSet = ids.ToList();
            if (idSet.Count == 0) return;
            await _dbSet.Where(s => idSet.Contains(s.Id)).ExecuteDeleteAsync();
        }

        public async Task<List<Sesion>> GetByIdsAsync(IEnumerable<Guid> ids)
        {
            var idSet = ids.ToList();
            if (idSet.Count == 0) return new List<Sesion>();
            return await _dbSet.AsNoTracking().Where(s => idSet.Contains(s.Id)).ToListAsync();
        }

        public async Task<List<Guid>> GetIdsByGrupoIdAsync(Guid grupoId)
            => await _dbSet.AsNoTracking().Where(s => s.GrupoId == grupoId).Select(s => s.Id).ToListAsync();

        public async Task<List<Guid>> GetIdsByAsignaturaIdAsync(Guid asignaturaId)
            => await _dbSet.AsNoTracking().Where(s => s.AsignaturaId == asignaturaId).Select(s => s.Id).ToListAsync();

        public async Task<List<Guid>> GetIdsByEspacioIdAsync(Guid espacioId)
            => await _dbSet.AsNoTracking().Where(s => s.EspacioId == espacioId).Select(s => s.Id).ToListAsync();
    }
}
