using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Infrastructure.Data.Configurations;

namespace SOEA.Infrastructure.Data.Context
{
    public class SOEABdContext : DbContext
    {
        public DbSet<Asignatura> Asignaturas { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Aplicar configuraciones desde IEntityTypeConfiguration
            modelBuilder.ApplyConfiguration(new AsignaturaConfiguration());
        }
        public DbSet<Docente> Docentes { get; set; }
        public DbSet<Espacio> Espacios { get; set; }
        public DbSet<Sesion> Sesiones { get; set; }

        public SOEABdContext(DbContextOptions<SOEABdContext> options) : base(options)
        {
        }
    }
}