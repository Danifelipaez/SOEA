using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class AsignaturaConfiguration : IEntityTypeConfiguration<Asignatura>
    {
        public void Configure(EntityTypeBuilder<Asignatura> builder)
        {
            builder.ToTable("Asignaturas");

            // Primary Key
            builder.HasKey(a => a.Id);

            // Properties
            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(a => a.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(a => a.Codigo)
                .HasColumnName("codigo")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(a => a.SesionesTeoriaPresencialSemana)
                .HasColumnName("sesiones_teoria_presencial_semana")
                .IsRequired();

            builder.Property(a => a.HorasTeoriaPresencial)
                .HasColumnName("horas_teoria_presencial")
                .IsRequired();

            builder.Property(a => a.SesionesTeoriaVirtualSemana)
                .HasColumnName("sesiones_teoria_virtual_semana")
                .IsRequired();

            builder.Property(a => a.HorasTeoriaVirtual)
                .HasColumnName("horas_teoria_virtual")
                .IsRequired();

            builder.Property(a => a.SesionesLaboratorioSemana)
                .HasColumnName("sesiones_laboratorio_semana")
                .IsRequired();

            builder.Property(a => a.HorasLaboratorio)
                .HasColumnName("horas_laboratorio")
                .IsRequired();

            builder.Property(a => a.SesionesLaboratorioSemestre)
                .HasColumnName("sesiones_laboratorio_semestre")
                .IsRequired();

            builder.Property(a => a.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(a => a.ProgramaId)
                .HasColumnName("programa_id")
                .IsRequired();

            builder.Property(a => a.EspacioFijoId)
                .HasColumnName("espacio_fijo_id")
                .IsRequired(false);

            builder.Property(a => a.Categoria)
                .HasColumnName("categoria")
                .HasConversion<string>()
                .HasDefaultValue(Domain.Enums.CategoriaAsignatura.Obligatoria)
                .IsRequired();

            builder.Property(a => a.HoraInicioMin)
                .HasColumnName("hora_inicio_min")
                .IsRequired(false);

            builder.Property(a => a.HoraFinMax)
                .HasColumnName("hora_fin_max")
                .IsRequired(false);

            builder.Property(a => a.EsCandidataAlternancia)
                .HasColumnName("es_candidata_alternancia")
                .HasDefaultValue(false)
                .IsRequired();

            // Alias legado de solo lectura (HorasPorSesion/SesionesPorSemana delegan a los campos
            // de teoría presencial) — no son columnas propias, EF no debe intentar mapearlos.
            builder.Ignore(a => a.HorasPorSesion);
            builder.Ignore(a => a.SesionesPorSemana);

            // Indexes
            builder.HasIndex(a => new { a.Codigo, a.ProgramaId })
                .IsUnique()
                .HasDatabaseName("ix_asignaturas_codigo_programa");

            builder.HasIndex(a => a.ProgramaId)
                .HasDatabaseName("ix_asignaturas_programa_id");
        }
    }
}
