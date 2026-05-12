using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class SesionConfiguration : IEntityTypeConfiguration<Sesion>
    {
        public void Configure(EntityTypeBuilder<Sesion> builder)
        {
            builder.ToTable("Sesiones");

            // Primary Key
            builder.HasKey(s => s.Id);

            // Properties
            builder.Property(s => s.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(s => s.AsignaturaId)
                .HasColumnName("asignatura_id")
                .IsRequired();

            builder.Property(s => s.DocenteId)
                .HasColumnName("docente_id")
                .IsRequired();

            builder.Property(s => s.BloqueTiempoId)
                .HasColumnName("bloque_tiempo_id")
                .IsRequired();

            builder.Property(s => s.EspacioId)
                .HasColumnName("espacio_id")
                .IsRequired(false);

            builder.Property(s => s.GrupoId)
                .HasColumnName("grupo_id")
                .IsRequired(false);

            builder.Property(s => s.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(s => s.Modalidad)
                .HasColumnName("modalidad")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(s => s.Estado)
                .HasColumnName("estado")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(s => s.DuracionHoras)
                .HasColumnName("duracion_horas")
                .HasColumnType("decimal(4,2)")
                .IsRequired();

            builder.Property(s => s.EsBloque)
                .HasColumnName("es_bloque")
                .IsRequired();

            builder.Property(s => s.EstaDividida)
                .HasColumnName("esta_dividida")
                .IsRequired();

            // Indexes
            builder.HasIndex(s => s.AsignaturaId)
                .HasDatabaseName("ix_sesion_asignatura_id");

            builder.HasIndex(s => s.DocenteId)
                .HasDatabaseName("ix_sesion_docente_id");

            builder.HasIndex(s => s.BloqueTiempoId)
                .HasDatabaseName("ix_sesion_bloque_tiempo_id");

            builder.HasIndex(s => s.EspacioId)
                .HasDatabaseName("ix_sesion_espacio_id");

            builder.HasIndex(s => new { s.DocenteId, s.BloqueTiempoId })
                .HasDatabaseName("ix_sesion_docente_bloque_conflicto");

            builder.HasIndex(s => new { s.EspacioId, s.BloqueTiempoId })
                .HasDatabaseName("ix_sesion_espacio_bloque_conflicto");
        }
    }
}
