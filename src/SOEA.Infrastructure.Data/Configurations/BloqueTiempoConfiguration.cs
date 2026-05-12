using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class BloqueTiempoConfiguration : IEntityTypeConfiguration<BloqueTiempo>
    {
        public void Configure(EntityTypeBuilder<BloqueTiempo> builder)
        {
            builder.ToTable("BloqueTiempos");

            // Primary Key
            builder.HasKey(b => b.Id);

            // Properties
            builder.Property(b => b.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(b => b.Dia)
                .HasColumnName("dia")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(b => b.HoraInicio)
                .HasColumnName("hora_inicio")
                .HasColumnType("time")
                .IsRequired();

            builder.Property(b => b.HoraFin)
                .HasColumnName("hora_fin")
                .HasColumnType("time")
                .IsRequired();

            // Indexes
            builder.HasIndex(b => b.Dia)
                .HasDatabaseName("ix_bloque_tiempo_dia");
        }
    }
}
