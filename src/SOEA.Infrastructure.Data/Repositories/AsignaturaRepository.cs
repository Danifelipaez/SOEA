using SOEA.Domain.Interfaces;       // ✅ La interfaz
using SOEA.Domain.Entities;         // ✅ La entidad
using SOEA.Infrastructure.Data.Context;  // ✅ El context
using Microsoft.EntityFrameworkCore;


namespace SOEA.Infrastructure.Data.Repositories
{
    public class AsignaturaRepository : IAsignaturaRepository
    {
        private readonly SOEABdContext _context;

        public AsignaturaRepository(SOEABdContext context)
        {
            _context = context;
        }
        public async Task AddAsync(Asignatura asignatura)
        {
            await _context.Asignaturas.AddAsync(asignatura);
            await _context.SaveChangesAsync();
        }
        public async Task<Asignatura?> GetByIdAsync(Guid id)
        {
            return await _context.Asignaturas.FindAsync(id);
        }
        public async Task<List<Asignatura>> GetAllAsync()
        {
            return await _context.Asignaturas.ToListAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var asignatura = await _context.Asignaturas.FindAsync(id);
            if (asignatura != null)
            {
                _context.Asignaturas.Remove(asignatura);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateAsync(Asignatura asignatura)
        {
            _context.Asignaturas.Update(asignatura);
            await _context.SaveChangesAsync();
        }
    }
}