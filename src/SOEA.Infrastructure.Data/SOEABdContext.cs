using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Infrastructure.Data.Configurations;

namespace SOEA.Infrastructure.Data.Context
{
    public class SOEABdContext : DbContext
    {
        public DbSet<Asignatura> Asignaturas { get; set; }
        public DbSet<Docente>    Docentes    { get; set; }
        public DbSet<Espacio>    Espacios    { get; set; }
        public DbSet<Sesion>     Sesiones    { get; set; }
        public DbSet<AsignacionSemanal> AsignacionesSemanales { get; set; }
        public DbSet<BloqueTiempo> BloqueTiempos { get; set; }
        public DbSet<Horario>    Horarios    { get; set; }
        public DbSet<Grupo>      Grupos      { get; set; }
        public DbSet<Facultad>   Facultades  { get; set; }
        public DbSet<Programa>   Programas   { get; set; }
        public DbSet<TipoAlternanciaConfig> TiposAlternancia { get; set; }

        public SOEABdContext(DbContextOptions<SOEABdContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Aplicar configuraciones desde IEntityTypeConfiguration
            modelBuilder.ApplyConfiguration(new AsignaturaConfiguration());
            modelBuilder.ApplyConfiguration(new DocenteConfiguration());
            modelBuilder.ApplyConfiguration(new EspacioConfiguration());
            modelBuilder.ApplyConfiguration(new BloqueTiempoConfiguration());
            modelBuilder.ApplyConfiguration(new SesionConfiguration());
            modelBuilder.ApplyConfiguration(new AsignacionSemanalConfiguration());
            modelBuilder.ApplyConfiguration(new HorarioConfiguration());
            modelBuilder.ApplyConfiguration(new GrupoConfiguration());
            modelBuilder.ApplyConfiguration(new FacultadConfiguration());
            modelBuilder.ApplyConfiguration(new ProgramaConfiguration());
            modelBuilder.ApplyConfiguration(new TipoAlternanciaConfigConfiguration());
        }
    }
}
