using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class DocenteRepositorio : BaseRepository<Docente>, IDocenteRepositorio
    {
        public DocenteRepositorio(SOEABdContext context) : base(context) { }

        // BUG auditoría (import Excel, InvalidOperationException al adjuntar un docente ya
        // existente): AsNoTracking() simple NO resuelve identidad — dos docentes que comparten un
        // mismo BloqueTiempo (p. ej. "Lunes 8am") salían con DOS instancias distintas del mismo
        // BloqueTiempo.Id. Al adjuntar el grafo desconectado de un docente al DbContext, la
        // segunda instancia con la misma llave choca contra la primera ya trackeada.
        // AsNoTrackingWithIdentityResolution() sí une por llave dentro del mismo query.
        public override async Task<List<Docente>> GetAllAsync()
            => await _dbSet.AsNoTrackingWithIdentityResolution().Include(d => d.BloquesDisponibles).ToListAsync();

        public async Task<Docente?> GetByCedulaAsync(string cedula)
            => await _dbSet.FirstOrDefaultAsync(d => d.CedulaIdentidad == cedula);

        public async Task<Docente?> GetByNombreAsync(string nombre)
            => await _dbSet.FirstOrDefaultAsync(d => EF.Functions.ILike(d.Nombre, nombre));
    }
}
