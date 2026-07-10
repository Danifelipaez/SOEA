using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class GrupoConfiguration : IEntityTypeConfiguration<Grupo>
    {
        public void Configure(EntityTypeBuilder<Grupo> builder)
        {
            builder.ToTable("Grupos");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(g => g.Codigo)
                .HasColumnName("codigo")
                .HasMaxLength(50)
                .IsRequired(false);

            builder.Property(g => g.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(g => g.ProgramaId)
                .HasColumnName("programa_id")
                .IsRequired();

            builder.Property(g => g.AsignaturaId)
                .HasColumnName("asignatura_id")
                .IsRequired(false);

            builder.Property(g => g.FacultadId)
                .HasColumnName("facultad_id")
                .IsRequired(false);

            builder.Property(g => g.Semestre)
                .HasColumnName("semestre")
                .IsRequired();

            builder.Property(g => g.EstudiantesInscritos)
                .HasColumnName("estudiantes_inscritos")
                .IsRequired();

            builder.Property(g => g.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            // Disponibilidad como JSON (misma estrategia que Docente)
            builder.Property(g => g.Disponibilidad)
                .HasColumnName("disponibilidad")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<FranjaHoraria>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
                .IsRequired(false);

            builder.Property(g => g.DisponibilidadUiJson)
                .HasColumnName("disponibilidad_ui_json")
                .IsRequired(false);

            // Índices
            builder.HasIndex(g => g.Codigo)
                .IsUnique()
                .HasFilter("codigo IS NOT NULL")
                .HasDatabaseName("ix_grupo_codigo");

            builder.HasIndex(g => g.ProgramaId)
                .HasDatabaseName("ix_grupo_programa_id");

            builder.HasIndex(g => g.AsignaturaId)
                .HasDatabaseName("ix_grupo_asignatura_id");

            builder.HasIndex(g => g.Nombre)
                .HasDatabaseName("ix_grupo_nombre");

            builder.HasIndex(g => g.Semestre)
                .HasDatabaseName("ix_grupo_semestre");
        }
    }
}
