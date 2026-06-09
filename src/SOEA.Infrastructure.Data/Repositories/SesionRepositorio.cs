using System;
using System.Collections.Generic;
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

        public async Task<bool> ExisteAsync(Guid asignaturaId, Guid docenteId, Guid bloqueTiempoId)
            => await _dbSet.AnyAsync(x =>
                x.AsignaturaId   == asignaturaId  &&
                x.DocenteId      == docenteId     &&
                x.BloqueTiempoId == bloqueTiempoId);
    }
}
