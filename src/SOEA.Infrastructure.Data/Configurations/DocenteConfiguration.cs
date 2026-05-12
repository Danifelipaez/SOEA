using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class DocenteConfiguration : IEntityTypeConfiguration<Docente>
    {
        public void Configure(EntityTypeBuilder<Docente> builder)
        {
            builder.ToTable("Docentes");

            // Primary Key
            builder.HasKey(d => d.Id);

            // Properties
            builder.Property(d => d.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(d => d.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(d => d.Apellido)
                .HasColumnName("apellido")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(d => d.Correo)
                .HasColumnName("correo")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(d => d.MaximoHorasSemanales)
                .HasColumnName("maximo_horas_semanales")
                .HasPrecision(5, 2)
                .IsRequired();

            builder.Property(d => d.Disponibilidad)
                .HasColumnName("disponibilidad")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<SOEA.Domain.Enums.FranjaHoraria>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
                .IsRequired();

            // Indexes
            builder.HasIndex(d => d.Correo)
                .IsUnique()
                .HasDatabaseName("ix_docentes_correo");
        }
    }
}
