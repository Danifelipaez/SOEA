using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class ProgramaRepositorio : BaseRepository<Programa>, IProgramaRepositorio
    {
        public ProgramaRepositorio(SOEABdContext context) : base(context) { }

        public async Task<Programa?> GetByNombreYFacultadAsync(string nombre, Guid facultadId)
            => await _dbSet.FirstOrDefaultAsync(x =>
                x.Nombre.ToLower() == nombre.ToLower() && x.FacultadId == facultadId);

        public Task<int> ContarPorFacultadAsync(Guid facultadId)
            => _dbSet.CountAsync(p => p.FacultadId == facultadId);
    }
}
