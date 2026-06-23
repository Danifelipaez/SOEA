using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class GrupoRepositorio : BaseRepository<Grupo>, IGrupoRepositorio
    {
        public GrupoRepositorio(SOEABdContext context) : base(context) { }

        public async Task<Grupo?> GetByNombreYProgramaAsync(string nombre, Guid programaId)
            => await _dbSet.FirstOrDefaultAsync(x =>
                EF.Functions.ILike(x.Nombre, nombre) && x.ProgramaId == programaId);

        public async Task<Grupo?> GetByCodigoAsync(string codigo)
            => await _dbSet.FirstOrDefaultAsync(x => x.Codigo == codigo);

        public async Task<IEnumerable<Grupo>> GetByAsignaturaIdAsync(Guid asignaturaId)
            => await _dbSet.Where(x => x.AsignaturaId == asignaturaId).ToListAsync();
    }
}
