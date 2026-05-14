using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class FacultadConfiguration : IEntityTypeConfiguration<Facultad>
    {
        public void Configure(EntityTypeBuilder<Facultad> builder)
        {
            builder.ToTable("Facultades");

            builder.HasKey(f => f.Id);

            builder.Property(f => f.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(f => f.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(255)
                .IsRequired();
        }
    }
}
