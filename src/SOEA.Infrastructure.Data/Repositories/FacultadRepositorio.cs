using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class FacultadRepositorio : BaseRepository<Facultad>, IFacultadRepositorio
    {
        public FacultadRepositorio(SOEABdContext context) : base(context) { }

        public async Task<Facultad?> GetByNombreAsync(string nombre)
            => await _dbSet.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Nombre, nombre));
    }
}
