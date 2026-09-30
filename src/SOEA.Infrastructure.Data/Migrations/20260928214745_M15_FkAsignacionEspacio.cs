using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M15_FkAsignacionEspacio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NEW-2 auditoría 2026-09-28. Saneamiento previo a la FK: borrar un espacio usado solo por
            // asignaciones generadas dejaba filas con espacio_id inexistente, y Program.cs migra al
            // arrancar — con huérfanos el ADD CONSTRAINT fallaría y el API no levantaría (mismo riesgo
            // que M14). Una sesión con aula inexistente no tiene lugar válido: se elimina completa
            // (sesión + todas sus asignaciones), que es lo que habría hecho el borrado correcto del
            // espacio (SesionCascadeService). Son datos regenerables de una corrida, no catálogo.
            migrationBuilder.Sql("""
                DELETE FROM "Sesiones" s
                WHERE EXISTS (SELECT 1 FROM "AsignacionesSemanales" a
                              WHERE a.sesion_id = s.id AND a.espacio_id IS NOT NULL
                                AND NOT EXISTS (SELECT 1 FROM "Espacios" e WHERE e.id = a.espacio_id));

                DELETE FROM "AsignacionesSemanales" a
                WHERE NOT EXISTS (SELECT 1 FROM "Sesiones" s WHERE s.id = a.sesion_id)
                   OR (a.espacio_id IS NOT NULL
                       AND NOT EXISTS (SELECT 1 FROM "Espacios" e WHERE e.id = a.espacio_id));
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AsignacionesSemanales_Espacios_espacio_id",
                table: "AsignacionesSemanales",
                column: "espacio_id",
                principalTable: "Espacios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AsignacionesSemanales_Espacios_espacio_id",
                table: "AsignacionesSemanales");
        }
    }
}
