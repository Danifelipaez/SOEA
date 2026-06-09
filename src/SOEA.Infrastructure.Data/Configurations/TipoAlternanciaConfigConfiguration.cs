using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class TipoAlternanciaConfigConfiguration : IEntityTypeConfiguration<TipoAlternanciaConfig>
    {
        public void Configure(EntityTypeBuilder<TipoAlternanciaConfig> builder)
        {
            builder.ToTable("TiposAlternancia");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(t => t.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.PatronBase)
                .HasColumnName("patron_base")
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            builder.Property(t => t.SemanasPresenciales)
                .HasColumnName("semanas_presenciales")
                .IsRequired();

            builder.Property(t => t.Color)
                .HasColumnName("color")
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(t => t.EsSistema)
                .HasColumnName("es_sistema")
                .IsRequired();

            builder.Property(t => t.Activo)
                .HasColumnName("activo")
                .IsRequired();

            // Seed de los 3 tipos de sistema (estables, no eliminables).
            builder.HasData(
                new
                {
                    Id = TipoAlternanciaConfig.IdTipoA,
                    Nombre = "Tipo A",
                    PatronBase = PatronBaseAlternancia.PresencialEnSemanaA,
                    SemanasPresenciales = 8,
                    Color = "#1565c0",
                    EsSistema = true,
                    Activo = true
                },
                new
                {
                    Id = TipoAlternanciaConfig.IdTipoB,
                    Nombre = "Tipo B",
                    PatronBase = PatronBaseAlternancia.PresencialEnSemanaB,
                    SemanasPresenciales = 8,
                    Color = "#e65100",
                    EsSistema = true,
                    Activo = true
                },
                new
                {
                    Id = TipoAlternanciaConfig.IdSinAlternancia,
                    Nombre = "Sin alternancia",
                    PatronBase = PatronBaseAlternancia.SinAlternancia,
                    SemanasPresenciales = 16,
                    Color = "#607d8b",
                    EsSistema = true,
                    Activo = true
                }
            );
        }
    }
}
