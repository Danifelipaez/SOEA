using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M17_ClavesAjenasCatalogoYAsignaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DB-6 auditoría 2026-09-28. Saneamiento previo a las FK (Program.cs migra al arrancar: con
            // huérfanos el ADD CONSTRAINT fallaría y el API no levantaría, igual que en M14/M15).
            // Solo se limpian datos derivados de una corrida, regenerables:
            //  1. Sesiones con una asignación en un bloque inexistente (sin lugar válido; mismo criterio
            //     que M15): se borran completas.
            //  2. Asignaciones sin sesión o con bloque inexistente (las de 1. ya quedaron sin sesión).
            // Programas.facultad_id y Asignaturas.programa_id son NOT NULL y son catálogo: un huérfano ahí
            // no se borra ni se inventa un padre — hay que corregirlo a mano, y la FK lo reporta por nombre
            // (en prod, 22 programas con facultad existente y 0 asignaturas, comprobado por la API).
            migrationBuilder.Sql("""
                DELETE FROM "Sesiones" s
                WHERE EXISTS (SELECT 1 FROM "AsignacionesSemanales" a
                              WHERE a.sesion_id = s.id
                                AND NOT EXISTS (SELECT 1 FROM "BloqueTiempos" b WHERE b.id = a.bloque_tiempo_id));

                DELETE FROM "AsignacionesSemanales" a
                WHERE NOT EXISTS (SELECT 1 FROM "Sesiones" s WHERE s.id = a.sesion_id)
                   OR NOT EXISTS (SELECT 1 FROM "BloqueTiempos" b WHERE b.id = a.bloque_tiempo_id);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AsignacionesSemanales_bloque_tiempo_id",
                table: "AsignacionesSemanales",
                column: "bloque_tiempo_id");

            migrationBuilder.AddForeignKey(
                name: "FK_AsignacionesSemanales_BloqueTiempos_bloque_tiempo_id",
                table: "AsignacionesSemanales",
                column: "bloque_tiempo_id",
                principalTable: "BloqueTiempos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AsignacionesSemanales_Sesiones_sesion_id",
                table: "AsignacionesSemanales",
                column: "sesion_id",
                principalTable: "Sesiones",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Asignaturas_Programas_programa_id",
                table: "Asignaturas",
                column: "programa_id",
                principalTable: "Programas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Programas_Facultades_facultad_id",
                table: "Programas",
                column: "facultad_id",
                principalTable: "Facultades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AsignacionesSemanales_BloqueTiempos_bloque_tiempo_id",
                table: "AsignacionesSemanales");

            migrationBuilder.DropForeignKey(
                name: "FK_AsignacionesSemanales_Sesiones_sesion_id",
                table: "AsignacionesSemanales");

            migrationBuilder.DropForeignKey(
                name: "FK_Asignaturas_Programas_programa_id",
                table: "Asignaturas");

            migrationBuilder.DropForeignKey(
                name: "FK_Programas_Facultades_facultad_id",
                table: "Programas");

            migrationBuilder.DropIndex(
                name: "IX_AsignacionesSemanales_bloque_tiempo_id",
                table: "AsignacionesSemanales");
        }
    }
}
