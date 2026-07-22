using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class CriterioCesionAlternanciaConfiguration : IEntityTypeConfiguration<CriterioCesionAlternancia>
    {
        public void Configure(EntityTypeBuilder<CriterioCesionAlternancia> builder)
        {
            builder.ToTable("CriteriosCesionAlternancia");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(c => c.Criterio)
                .HasColumnName("criterio")
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            builder.Property(c => c.Orden)
                .HasColumnName("orden")
                .IsRequired();

            builder.Property(c => c.Activo)
                .HasColumnName("activo")
                .HasDefaultValue(true)
                .IsRequired();

            // Seed de los 4 criterios de sistema: orden inicial MultiplesSesiones → Electiva → Optativa
            // → Elegible, todos activos.
            builder.HasData(
                new
                {
                    Id = CriterioCesionAlternancia.IdMultiplesSesiones,
                    Criterio = CriterioElegibilidadAlternancia.MultiplesSesiones,
                    Orden = 1,
                    Activo = true
                },
                new
                {
                    Id = CriterioCesionAlternancia.IdElectiva,
                    Criterio = CriterioElegibilidadAlternancia.Electiva,
                    Orden = 2,
                    Activo = true
                },
                new
                {
                    Id = CriterioCesionAlternancia.IdOptativa,
                    Criterio = CriterioElegibilidadAlternancia.Optativa,
                    Orden = 3,
                    Activo = true
                },
                new
                {
                    Id = CriterioCesionAlternancia.IdElegible,
                    Criterio = CriterioElegibilidadAlternancia.Elegible,
                    Orden = 4,
                    Activo = true
                }
            );
        }
    }
}
