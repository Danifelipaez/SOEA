using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Context
{
    public class SOEABdContext : DbContext
    {
        public DbSet<Asignatura> Asignaturas { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Configuraciones adicionales de entidades si es necesario
            ConfigureAsignatura(modelBuilder);
        }
        private static void ConfigureAsignatura(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Asignatura>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(a => a.Nombre)
                    .IsRequired()
                    .HasMaxLength(255);
                entity.Property(a => a.Codigo)
                    .IsRequired()
                    .HasMaxLength(20);
                entity.Property(a => a.BloquesSemanales)
                    .IsRequired();
                entity.Property(a => a.RequiereLab)
                    .IsRequired()
                    .HasDefaultValue(false);
                entity.Property(a => a.Alternancia)
                    .IsRequired()
                    .HasConversion<string>();
                entity.Property(a => a.ProgramaId)
                    .IsRequired();
            });
        }
        public DbSet<Docente> Docentes { get; set; }
        public DbSet<Espacio> Espacios { get; set; }
        public DbSet<Sesion> Sesiones { get; set; }

        public SOEABdContext(DbContextOptions<SOEABdContext> options) : base(options)
        {
        }
    }
}