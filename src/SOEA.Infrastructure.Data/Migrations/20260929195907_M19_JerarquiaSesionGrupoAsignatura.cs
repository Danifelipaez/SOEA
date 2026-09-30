using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M19_JerarquiaSesionGrupoAsignatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Jerarquía obligatoria Sesión → Grupo → Asignatura → Programa → Facultad.
            // Saneamiento previo (misma estrategia que M14: corre en la transacción de la migración):
            //  1. Grupos sin asignatura: nunca se pudieron programar (la generación los descarta) y la API
            //     ya no deja crearlos. Se borran con sus sesiones.
            //  2. Sesiones sin grupo: si su asignatura tiene UN solo grupo, es ese; si no, no hay forma
            //     de saber de qué grupo eran y se borran.
            //  3. Sesiones de una asignatura distinta a la de su grupo: dato contradictorio, se borran.
            //  (Las AsignacionesSemanales caen en cascada con su sesión; los ids borrados se quitan de
            //  Horarios.sesion_ids.)
            // Grupos.programa_id/facultad_id se eliminan: se derivan de la asignatura y podían contradecirla.
            migrationBuilder.Sql("""
                DELETE FROM "Sesiones" s USING "Grupos" g WHERE s.grupo_id = g.id AND g.asignatura_id IS NULL;
                DELETE FROM "Grupos" WHERE asignatura_id IS NULL;

                UPDATE "Sesiones" s SET grupo_id = u.grupo_id
                FROM (SELECT asignatura_id, min(id::text)::uuid AS grupo_id
                      FROM "Grupos" GROUP BY asignatura_id HAVING count(*) = 1) u
                WHERE s.grupo_id IS NULL AND u.asignatura_id = s.asignatura_id;

                DELETE FROM "Sesiones" s
                WHERE s.grupo_id IS NULL
                   OR NOT EXISTS (SELECT 1 FROM "Grupos" g WHERE g.id = s.grupo_id AND g.asignatura_id = s.asignatura_id);

                UPDATE "Horarios" h SET sesion_ids = (
                    SELECT coalesce(json_agg(t.e ORDER BY t.o), '[]'::json)::text
                    FROM jsonb_array_elements_text(h.sesion_ids::jsonb) WITH ORDINALITY AS t(e, o)
                    WHERE EXISTS (SELECT 1 FROM "Sesiones" s WHERE s.id::text = t.e));
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Facultades_facultad_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Programas_programa_id",
                table: "Grupos");

            migrationBuilder.DropIndex(
                name: "ix_grupo_programa_id",
                table: "Grupos");

            migrationBuilder.DropIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "facultad_id",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "programa_id",
                table: "Grupos");

            migrationBuilder.AlterColumn<Guid>(
                name: "grupo_id",
                table: "Sesiones",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "asignatura_id",
                table: "Grupos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // La sesión es de la asignatura de su grupo: FK compuesta (grupo_id, asignatura_id). No se
            // modela en EF (ver GrupoConfiguration). ON UPDATE CASCADE: si el grupo cambia de
            // asignatura, sus sesiones cambian con él.
            migrationBuilder.Sql("""
                ALTER TABLE "Grupos" ADD CONSTRAINT "AK_Grupos_id_asignatura_id" UNIQUE (id, asignatura_id);
                ALTER TABLE "Sesiones" ADD CONSTRAINT "FK_Sesiones_Grupos_grupo_id_asignatura_id"
                    FOREIGN KEY (grupo_id, asignatura_id) REFERENCES "Grupos" (id, asignatura_id)
                    ON UPDATE CASCADE ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Sesiones" DROP CONSTRAINT "FK_Sesiones_Grupos_grupo_id_asignatura_id";
                ALTER TABLE "Grupos" DROP CONSTRAINT "AK_Grupos_id_asignatura_id";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "grupo_id",
                table: "Sesiones",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "asignatura_id",
                table: "Grupos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "facultad_id",
                table: "Grupos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "programa_id",
                table: "Grupos",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Grupos" g SET programa_id = a.programa_id, facultad_id = p.facultad_id
                FROM "Asignaturas" a JOIN "Programas" p ON p.id = a.programa_id
                WHERE a.id = g.asignatura_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "programa_id",
                table: "Grupos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_grupo_programa_id",
                table: "Grupos",
                column: "programa_id");

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos",
                column: "facultad_id");

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
        }
    }
}
