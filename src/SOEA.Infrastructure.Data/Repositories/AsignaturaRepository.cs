using SOEA.Application.Interfaces;  // ✅ La interfaz
using SOEA.Domain.Entities;         // ✅ La entidad
using SOEA.Infrastructure.Data.Context;  // ✅ El context
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices; // ✅ EF Core


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
    }
}