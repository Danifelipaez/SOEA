using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class HorarioConfiguration : IEntityTypeConfiguration<Horario>
    {
        public void Configure(EntityTypeBuilder<Horario> builder)
        {
            builder.ToTable("Horarios");

            // Primary Key
            builder.HasKey(h => h.Id);

            // Properties
            builder.Property(h => h.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(h => h.Semestre)
                .HasColumnName("semestre")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(h => h.GeneratedAt)
                .HasColumnName("generated_at")
                .IsRequired();

            builder.Property(h => h.Estado)
                .HasColumnName("estado")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(h => h.SesioneIds)
                .HasColumnName("sesion_ids")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
                .IsRequired();

            builder.Property(h => h.HardConstraintViolations)
                .HasColumnName("hard_constraint_violations")
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(h => h.SoftConstraintFitnessScore)
                .HasColumnName("soft_constraint_fitness_score")
                .HasPrecision(10, 4)
                .IsRequired()
                .HasDefaultValue(0m);

            // Indexes
            builder.HasIndex(h => h.Semestre)
                .HasDatabaseName("ix_horario_semestre");

            builder.HasIndex(h => h.Estado)
                .HasDatabaseName("ix_horario_estado");

            builder.HasIndex(h => h.GeneratedAt)
                .HasDatabaseName("ix_horario_generated_at");
        }
    }
}
