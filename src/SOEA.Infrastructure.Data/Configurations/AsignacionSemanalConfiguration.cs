using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class AsignacionSemanalConfiguration : IEntityTypeConfiguration<AsignacionSemanal>
    {
        public void Configure(EntityTypeBuilder<AsignacionSemanal> builder)
        {
            builder.ToTable("AsignacionesSemanales");

            // Primary Key
            builder.HasKey(a => a.Id);

            // Properties
            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(a => a.SesionId)
                .HasColumnName("sesion_id")
                .IsRequired();

            builder.Property(a => a.Semana)
                .HasColumnName("semana")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(a => a.BloqueTiempoId)
                .HasColumnName("bloque_tiempo_id")
                .IsRequired();

            builder.Property(a => a.EspacioId)
                .HasColumnName("espacio_id")
                .IsRequired(false);

            builder.Property(a => a.Modalidad)
                .HasColumnName("modalidad")
                .HasConversion<string>()
                .IsRequired();

            // Indexes
            builder.HasIndex(a => a.SesionId)
                .HasDatabaseName("ix_asignacion_semanal_sesion_id");

            builder.HasIndex(a => new { a.SesionId, a.Semana })
                .IsUnique()
                .HasDatabaseName("ux_asignacion_semanal_sesion_semana");

            builder.HasIndex(a => new { a.EspacioId, a.Semana, a.BloqueTiempoId })
                .HasDatabaseName("ix_asignacion_semanal_espacio_conflicto");
        }
    }
}
