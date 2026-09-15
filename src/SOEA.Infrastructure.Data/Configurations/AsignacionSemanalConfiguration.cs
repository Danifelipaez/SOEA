using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOEA.Domain.Entities;

namespace SOEA.Infrastructure.Data.Configurations
{
    public class AsignacionSemanalConfiguration : IEntityTypeConfiguration<AsignacionSemanal>
    {
        public void Configure(EntityTypeBuilder<AsignacionSemanal> builder)
        {
            builder.ToTable("AsignacionesSemanales");

            // Primary Key
            builder.HasKey(a => a.Id);

            // Properties
            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(a => a.SesionId)
                .HasColumnName("sesion_id")
                .IsRequired();

            builder.Property(a => a.Semana)
                .HasColumnName("semana")
                .HasConversion<string>()
                .IsRequired();

            builder.Property(a => a.BloqueTiempoId)
                .HasColumnName("bloque_tiempo_id")
                .IsRequired();

            builder.Property(a => a.EspacioId)
                .HasColumnName("espacio_id")
                .IsRequired(false);

            builder.Property(a => a.Modalidad)
                .HasColumnName("modalidad")
                .HasConversion<string>()
                .IsRequired();

            // Indexes
            builder.HasIndex(a => a.SesionId)
                .HasDatabaseName("ix_asignacion_semanal_sesion_id");

            builder.HasIndex(a => new { a.SesionId, a.Semana })
                .IsUnique()
                .HasDatabaseName("ux_asignacion_semanal_sesion_semana");

            // M8 auditoría: antes solo de performance (no única) — dos sesiones distintas podían
            // reservar el mismo espacio en el mismo bloque/semana si dos requests concurrentes
            // pasaban la validación en memoria antes de que cualquiera de las dos escribiera.
            // Red de seguridad a nivel de BD contra doble reserva; EspacioId nulo (sesión
            // virtual) queda fuera del filtro porque muchas sesiones virtuales comparten
            // EspacioId=null en el mismo bloque legítimamente.
            //
            // LÍMITE CONOCIDO (colapso a una fila por sesión): este índice NO ve el choque entre
            // una sesión que no alterna (fila en la semana A, pero ocupa el aula también en la B)
            // y un miembro TipoB (fila en la B), porque los valores de `semana` difieren. No se
            // puede endurecer: una pareja de alternancia comparte legítimamente (espacio, bloque)
            // entre semanas y SQL no ve pareja_alternancia_id. La garantía real vive en HC-S01 de
            // ValidadorRestriccionesDuras (que expande cada sesión a las semanas que de verdad
            // ocupa, vía ModalidadSemanal.SemanasQueOcupanEspacio), más el NoOverlap por
            // (espacio, semana) de MotorConstraintProgramming y AsignadorEspaciosExactoCpSat.
            builder.HasIndex(a => new { a.EspacioId, a.Semana, a.BloqueTiempoId })
                .IsUnique()
                .HasFilter("espacio_id IS NOT NULL")
                .HasDatabaseName("ux_asignacion_semanal_espacio_conflicto");
        }
    }
}
