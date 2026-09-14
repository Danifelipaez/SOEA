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

            builder.Property(g => g.DocenteId)
                .HasColumnName("docente_id")
                .IsRequired(false);

            builder.Property(g => g.EstudiantesInscritos)
                .HasColumnName("estudiantes_inscritos")
                .IsRequired();

            builder.Property(g => g.Alternancia)
                .HasColumnName("alternancia")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(g => g.DisponibilidadUiJson)
                .HasColumnName("disponibilidad_ui_json")
                .IsRequired(false);

            // Requisitos de espacio por tipo de sesión, como JSON (misma estrategia que Disponibilidad).
            // P1_GrupoComoEje sólo hizo backfill de esta columna para grupos cuya asignatura tenía
            // espacio_fijo_id — el resto quedó NULL, y Deserialize<T> lanza ArgumentNullException
            // con input null. Grupo con NULL == "sin requisitos de espacio", igual que lista vacía.
            // UsePropertyAccessMode(Property) es obligatorio: sin él, EF detecta el backing field
            // "_requisitosEspacio" por convención de nombres y lo escribe directo, saltándose el
            // guard "?? new()" del setter de Grupo.RequisitosEspacio.
            builder.Property(g => g.RequisitosEspacio)
                .HasColumnName("requisitos_espacio")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => DeserializarRequisitosEspacio(v))
                .UsePropertyAccessMode(PropertyAccessMode.Property)
                .IsRequired(false);
            // M16 auditoría: sin ValueComparer, EF Core solo detecta un cambio en esta colección
            // por REASIGNACIÓN de referencia — hoy funciona porque ActualizarRequisitosEspacio
            // siempre asigna una lista nueva, pero un futuro `RequisitosEspacio.Add(...)` mutando
            // in-place se descartaría en silencio al guardar, sin ningún error. RequisitoEspacio es
            // un record (igualdad estructural), así que SequenceEqual ya compara por valor.
            builder.Property(g => g.RequisitosEspacio).Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<SOEA.Domain.ValueObjects.RequisitoEspacio>>(
                    (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                    v => (v ?? new()).Aggregate(0, (hash, r) => HashCode.Combine(hash, r.GetHashCode())),
                    v => (v ?? new()).ToList()));

            // M14 auditoría (Decisión 1 del saneamiento): ProgramaId/AsignaturaId/DocenteId no
            // tenían FK — AsignaturaService.DeleteAsync ya cascadea a Grupos a mano, y
            // DocenteService.DeleteAsync ya bloquea el borrado si hay Grupos asociados; estas FK
            // son la red de seguridad para cualquier otro camino de borrado que no pase por esos
            // dos servicios. Restrict en los tres: un Grupo vivo nunca debe quedar sin su
            // Programa/Asignatura/Docente por un borrado que no lo previó.
            // FacultadId → SetNull: a diferencia de los otros tres, esta columna no tiene ningún
            // invariante de negocio que dependa de ella (verificado: solo se lee para mostrarla en
            // el catálogo) y, en la BD local, el 100% de las filas existentes tenían un valor
            // huérfano (bug de import corregido aparte) — SetNull permite sanear esos datos sin
            // tener que inventar una facultad "correcta" para cada fila histórica.
            builder.HasOne<Programa>()
                .WithMany()
                .HasForeignKey(g => g.ProgramaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Asignatura>()
                .WithMany()
                .HasForeignKey(g => g.AsignaturaId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Docente>()
                .WithMany()
                .HasForeignKey(g => g.DocenteId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Facultad>()
                .WithMany()
                .HasForeignKey(g => g.FacultadId)
                .OnDelete(DeleteBehavior.SetNull);

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

            builder.HasIndex(g => g.DocenteId)
                .HasDatabaseName("ix_grupo_docente_id");
        }

        public static List<SOEA.Domain.ValueObjects.RequisitoEspacio> DeserializarRequisitosEspacio(string? json) =>
            string.IsNullOrEmpty(json)
                ? new List<SOEA.Domain.ValueObjects.RequisitoEspacio>()
                : System.Text.Json.JsonSerializer.Deserialize<List<SOEA.Domain.ValueObjects.RequisitoEspacio>>(json, (System.Text.Json.JsonSerializerOptions?)null) ?? new();
    }
}
