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

            // NEW-3 auditoría 2026-09-28: el horario (corrida) dueño de la fila. Nullable: las filas
            // previas a M16 se rellenan por migración y, en Postgres, un NULL no choca con otro NULL.
            // Restrict: los Horarios nunca se borran (solo sus sesiones al regenerar).
            builder.Property(a => a.HorarioId)
                .HasColumnName("horario_id")
                .IsRequired(false);

            builder.HasOne<Horario>()
                .WithMany()
                .HasForeignKey(a => a.HorarioId)
                .OnDelete(DeleteBehavior.Restrict);

            // DB-6 auditoría 2026-09-28: las dos claves que faltaban (espacio_id y horario_id ya tienen FK).
            //   - sesion_id → Cascade: la asignación es la materialización de una sesión y no tiene
            //     sentido sin ella; antes, borrar una sesión sin borrar antes sus asignaciones (siempre
            //     a mano, en SesionCascadeService/GenerarHorarioService) dejaba filas huérfanas.
            //   - bloque_tiempo_id → Restrict, igual que Sesion.BloqueTiempoId.
            builder.HasOne<Sesion>()
                .WithMany()
                .HasForeignKey(a => a.SesionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<BloqueTiempo>()
                .WithMany()
                .HasForeignKey(a => a.BloqueTiempoId)
                .OnDelete(DeleteBehavior.Restrict);

            // NEW-2 auditoría 2026-09-28: sin FK, borrar un espacio usado por el horario respondía 204
            // y dejaba filas apuntando a un aula inexistente. Restrict: borrar el aula exige purgar
            // antes las sesiones que la ocupan (SesionCascadeService), no dejar referencias colgando.
            // Sin navegación inversa, igual que las FK de Sesion (M14). El índice único
            // (espacio_id, semana, bloque) ya cubre espacio_id como prefijo.
            builder.HasOne<Espacio>()
                .WithMany()
                .HasForeignKey(a => a.EspacioId)
                .OnDelete(DeleteBehavior.Restrict);

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
            // NEW-3: acotado por horario. Antes era (espacio, semana, bloque) a secas, así que dos
            // semestres distintos —cada uno con su horario vigente— no podían coexistir: generar
            // "2026-2" con "2026-1" vigente respondía un 409 genérico (23505). Dentro de un mismo
            // horario la garantía es la misma de siempre.
            builder.HasIndex(a => new { a.HorarioId, a.EspacioId, a.Semana, a.BloqueTiempoId })
                .IsUnique()
                .HasFilter("espacio_id IS NOT NULL")
                .HasDatabaseName("ux_asignacion_semanal_espacio_conflicto");
        }
    }
}
