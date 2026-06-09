using System;
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
    }
}
