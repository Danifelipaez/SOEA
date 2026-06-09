using System;
using System.Threading.Tasks;
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

        public async Task<Asignatura?> GetByCodigoYProgramaAsync(string codigo, Guid programaId)
            => await _dbSet.FirstOrDefaultAsync(a => a.Codigo == codigo && a.ProgramaId == programaId);

        public async Task<Asignatura?> GetByNombreYProgramaAsync(string nombre, Guid programaId)
            => await _dbSet.FirstOrDefaultAsync(a =>
                EF.Functions.ILike(a.Nombre, nombre) && a.ProgramaId == programaId);
    }
}
