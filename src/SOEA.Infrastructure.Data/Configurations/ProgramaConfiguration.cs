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

            // DB-6 auditoría 2026-09-28: sin FK, DELETE /facultades/{id} respondía 204 y dejaba los
            // programas apuntando a una facultad inexistente. Restrict: FacultadService.DeleteAsync
            // avisa con un mensaje claro antes de llegar aquí.
            builder.HasOne<Facultad>()
                .WithMany()
                .HasForeignKey(p => p.FacultadId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(p => p.FacultadId)
                .HasDatabaseName("ix_programas_facultad_id");
        }
    }
}
