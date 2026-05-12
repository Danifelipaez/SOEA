using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class EspacioConfiguration : IEntityTypeConfiguration<Espacio>
    {
        public void Configure(EntityTypeBuilder<Espacio> builder)
        {
            builder.ToTable("Espacios");

            // Primary Key
            builder.HasKey(e => e.Id);

            // Properties
            builder.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(e => e.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(e => e.Tipo)
                .HasColumnName("tipo")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(e => e.Capacidad)
                .HasColumnName("capacidad")
                .IsRequired();

            builder.Property(e => e.Edificio)
                .HasColumnName("edificio")
                .HasMaxLength(100)
                .IsRequired(false);

            builder.Property(e => e.Piso)
                .HasColumnName("piso")
                .IsRequired(false);

            // Indexes
            builder.HasIndex(e => e.Tipo)
                .HasDatabaseName("ix_espacios_tipo");
        }
    }
}
