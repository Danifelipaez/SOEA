using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M14_ClavesAjenasSesionesYGrupos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Saneamiento previo a las FK (P0-1, auditoría 2026-09-14). Con datos huérfanos el ADD
            // CONSTRAINT falla y, como Program.cs migra al arrancar, el API no llega a levantar.
            // Corre dentro de la transacción de la migración: si algo falla no queda nada a medias.
            //  1. Sesiones fuera de todo Horario.sesion_ids: filas del import de Excel y sesiones
            //     manuales anteriores al fix P0-5. Ninguna pantalla las mostraba, pero bloqueaban docentes.
            //  2. Sesion.bloque_tiempo_id guardaba la pista de Fase 1, no el bloque final (P0-4): se
            //     alinea con su asignación.
            //  3. Sesiones sin asignatura o bloque existente (columnas NOT NULL, nada que conservar) y
            //     asignaciones sin sesión.
            //  4. Referencias opcionales rotas → NULL.
            // Grupos.programa_id (NOT NULL) no se toca: un huérfano ahí hay que corregirlo a mano, y la
            // FK lo reporta por nombre.
            migrationBuilder.Sql("""
                DELETE FROM "Sesiones" s
                WHERE NOT EXISTS (SELECT 1 FROM "Horarios" h, jsonb_array_elements_text(h.sesion_ids::jsonb) e
                                  WHERE e = s.id::text);

                UPDATE "Sesiones" s SET bloque_tiempo_id = a.bloque_tiempo_id
                FROM "AsignacionesSemanales" a
                WHERE a.sesion_id = s.id AND a.bloque_tiempo_id <> s.bloque_tiempo_id
                  AND EXISTS (SELECT 1 FROM "BloqueTiempos" b WHERE b.id = a.bloque_tiempo_id);

                DELETE FROM "Sesiones" s
                WHERE NOT EXISTS (SELECT 1 FROM "Asignaturas" x WHERE x.id = s.asignatura_id)
                   OR NOT EXISTS (SELECT 1 FROM "BloqueTiempos" x WHERE x.id = s.bloque_tiempo_id);

                DELETE FROM "AsignacionesSemanales" a
                WHERE NOT EXISTS (SELECT 1 FROM "Sesiones" s WHERE s.id = a.sesion_id);

                UPDATE "Sesiones" s SET grupo_id = NULL
                WHERE grupo_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Grupos" x WHERE x.id = s.grupo_id);
                UPDATE "Sesiones" s SET espacio_id = NULL
                WHERE espacio_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Espacios" x WHERE x.id = s.espacio_id);
                UPDATE "Sesiones" s SET docente_id = NULL
                WHERE docente_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Docentes" x WHERE x.id = s.docente_id);
                UPDATE "Grupos" g SET facultad_id = NULL
                WHERE facultad_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Facultades" x WHERE x.id = g.facultad_id);
                UPDATE "Grupos" g SET asignatura_id = NULL
                WHERE asignatura_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Asignaturas" x WHERE x.id = g.asignatura_id);
                UPDATE "Grupos" g SET docente_id = NULL
                WHERE docente_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Docentes" x WHERE x.id = g.docente_id);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Sesiones_grupo_id",
                table: "Sesiones",
                column: "grupo_id");

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos",
                column: "facultad_id");

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Asignaturas_asignatura_id",
                table: "Grupos",
                column: "asignatura_id",
                principalTable: "Asignaturas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Docentes_docente_id",
                table: "Grupos",
                column: "docente_id",
                principalTable: "Docentes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Facultades_facultad_id",
                table: "Grupos",
                column: "facultad_id",
                principalTable: "Facultades",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Programas_programa_id",
                table: "Grupos",
                column: "programa_id",
                principalTable: "Programas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Asignaturas_asignatura_id",
                table: "Sesiones",
                column: "asignatura_id",
                principalTable: "Asignaturas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_BloqueTiempos_bloque_tiempo_id",
                table: "Sesiones",
                column: "bloque_tiempo_id",
                principalTable: "BloqueTiempos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Docentes_docente_id",
                table: "Sesiones",
                column: "docente_id",
                principalTable: "Docentes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Espacios_espacio_id",
                table: "Sesiones",
                column: "espacio_id",
                principalTable: "Espacios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Grupos_grupo_id",
                table: "Sesiones",
                column: "grupo_id",
                principalTable: "Grupos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Asignaturas_asignatura_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Docentes_docente_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Facultades_facultad_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Programas_programa_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Asignaturas_asignatura_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_BloqueTiempos_bloque_tiempo_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Docentes_docente_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Espacios_espacio_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Grupos_grupo_id",
                table: "Sesiones");

            migrationBuilder.DropIndex(
                name: "IX_Sesiones_grupo_id",
                table: "Sesiones");

            migrationBuilder.DropIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos");
        }
    }
}
