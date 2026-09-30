using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M16_AsignacionPorHorario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales");

            migrationBuilder.AddColumn<Guid>(
                name: "horario_id",
                table: "AsignacionesSemanales",
                type: "uuid",
                nullable: true);

            // NEW-3 auditoría 2026-09-28. Rellena el horario de las filas existentes desde
            // Horarios.sesion_ids (mismo cruce que M14). Las filas que no salen en ningún horario
            // quedan en NULL: en Postgres un NULL no choca con otro NULL, así que no rompen el índice.
            // Va antes de la FK, con el horario que ya existe por construcción.
            migrationBuilder.Sql("""
                UPDATE "AsignacionesSemanales" a SET horario_id = h.id
                FROM "Horarios" h, jsonb_array_elements_text(h.sesion_ids::jsonb) e
                WHERE e = a.sesion_id::text;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AsignacionesSemanales_espacio_id",
                table: "AsignacionesSemanales",
                column: "espacio_id");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales",
                columns: new[] { "horario_id", "espacio_id", "semana", "bloque_tiempo_id" },
                unique: true,
                filter: "espacio_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AsignacionesSemanales_Horarios_horario_id",
                table: "AsignacionesSemanales",
                column: "horario_id",
                principalTable: "Horarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AsignacionesSemanales_Horarios_horario_id",
                table: "AsignacionesSemanales");

            migrationBuilder.DropIndex(
                name: "IX_AsignacionesSemanales_espacio_id",
                table: "AsignacionesSemanales");

            migrationBuilder.DropIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales");

            migrationBuilder.DropColumn(
                name: "horario_id",
                table: "AsignacionesSemanales");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales",
                columns: new[] { "espacio_id", "semana", "bloque_tiempo_id" },
                unique: true,
                filter: "espacio_id IS NOT NULL");
        }
    }
}
