using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class ProgramaConfiguration : IEntityTypeConfiguration<Programa>
    {
        public void Configure(EntityTypeBuilder<Programa> builder)
        {
            builder.ToTable("Programas");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(p => p.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(p => p.FacultadId)
                .HasColumnName("facultad_id")
                .IsRequired();

            builder.HasIndex(p => p.FacultadId)
                .HasDatabaseName("ix_programas_facultad_id");
        }
    }
}
