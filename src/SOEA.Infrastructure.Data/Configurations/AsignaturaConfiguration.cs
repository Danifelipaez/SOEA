using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class AsignaturaConfiguration : IEntityTypeConfiguration<Asignatura>
    {
        public void Configure(EntityTypeBuilder<Asignatura> builder)
        {
            builder.ToTable("Asignaturas");

            // Primary Key
            builder.HasKey(a => a.Id);

            // Properties
            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(a => a.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(a => a.Codigo)
                .HasColumnName("codigo")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(a => a.HorasPorSesion)
                .HasColumnName("horas_por_sesion")
                .IsRequired();

            builder.Property(a => a.SesionesPorSemana)
                .HasColumnName("sesiones_por_semana")
                .IsRequired();

            builder.Property(a => a.SesionesLaboratorioSemestre)
                .HasColumnName("sesiones_laboratorio_semestre")
                .IsRequired();

            builder.Property(a => a.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(a => a.ProgramaId)
                .HasColumnName("programa_id")
                .IsRequired();

            builder.Property(a => a.DocenteId)
                .HasColumnName("docente_id")
                .IsRequired(false);

            builder.Property(a => a.EspacioFijoId)
                .HasColumnName("espacio_fijo_id")
                .IsRequired(false);

            // Indexes
            builder.HasIndex(a => new { a.Codigo, a.ProgramaId })
                .IsUnique()
                .HasDatabaseName("ix_asignaturas_codigo_programa");

            builder.HasIndex(a => a.ProgramaId)
                .HasDatabaseName("ix_asignaturas_programa_id");
        }
    }
}
