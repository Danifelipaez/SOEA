using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class GrupoConfiguration : IEntityTypeConfiguration<Grupo>
    {
        public void Configure(EntityTypeBuilder<Grupo> builder)
        {
            builder.ToTable("Grupos");

            // Primary Key
            builder.HasKey(g => g.Id);

            // Properties
            builder.Property(g => g.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(g => g.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(g => g.ProgramaId)
                .HasColumnName("programa_id")
                .IsRequired();

            builder.Property(g => g.Semestre)
                .HasColumnName("semestre")
                .IsRequired();

            builder.Property(g => g.EstudiantesInscritos)
                .HasColumnName("estudiantes_inscritos")
                .IsRequired();

            builder.Property(g => g.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            // Indexes
            builder.HasIndex(g => g.ProgramaId)
                .HasDatabaseName("ix_grupo_programa_id");

            builder.HasIndex(g => g.Nombre)
                .HasDatabaseName("ix_grupo_nombre");

            builder.HasIndex(g => g.Semestre)
                .HasDatabaseName("ix_grupo_semestre");
        }
    }
}
