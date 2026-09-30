using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Repositories
{
    public class DocenteRepositorio : BaseRepository<Docente>, IDocenteRepositorio
    {
        public DocenteRepositorio(SOEABdContext context) : base(context) { }

        public override async Task<List<Docente>> GetAllAsync()
            => await _dbSet.AsNoTracking().Include(d => d.BloquesDisponibles).ToListAsync();

        // L-2 auditoría 2026-09-28: FindAsync no carga BloquesDisponibles (solo GetAllAsync lo hacía), así
        // que AsignarDocenteSesionService veía siempre la lista vacía y saltaba el aviso de
        // disponibilidad. Con tracking, como FindAsync: UpdateAsync/Update sigue funcionando igual.
        public override async Task<Docente?> GetByIdAsync(Guid id)
            => await _dbSet.Include(d => d.BloquesDisponibles).FirstOrDefaultAsync(d => d.Id == id);

        // Update() marca como Modified todo el grafo alcanzable, incluidos los BloqueTiempo del catálogo que
        // cuelgan de BloquesDisponibles: editar un docente reescribía filas de la grilla institucional. Los
        // bloques vuelven a Unchanged; las filas de unión (DisponibilidadDocente) conservan su estado, así que
        // un bloque recién añadido (importación) se sigue insertando.
        public override async Task UpdateAsync(Docente entity)
        {
            _dbSet.Update(entity);
            foreach (var bloque in entity.BloquesDisponibles)
                _context.Entry(bloque).State = EntityState.Unchanged;
            await _context.SaveChangesAsync();
        }

        public async Task<Docente?> GetByCedulaAsync(string cedula)
            => await _dbSet.FirstOrDefaultAsync(d => d.CedulaIdentidad == cedula);

        public async Task<Docente?> GetByNombreAsync(string nombre)
            => await _dbSet.FirstOrDefaultAsync(d => d.Nombre.ToLower() == nombre.ToLower());
    }
}
