using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Fase 2: el docente pasa de Asignatura a Grupo, y Grupo pierde Semestre.
    /// El paso de datos preserva la información existente: antes de borrar
    /// Asignaturas.docente_id, se copia hacia los Grupos (uno por asignatura con docente).
    /// NO fusiona las asignaturas duplicadas — eso lo hace un script separado y revisable
    /// (docs/Fase2_FusionAsignaturasDuplicadas.sql), corrido a mano tras probar en una copia.
    /// </summary>
    public partial class Fase2DocenteEnGrupo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Esquema: agregar Grupos.docente_id + índice ──────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "docente_id",
                table: "Grupos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_grupo_docente_id",
                table: "Grupos",
                column: "docente_id");

            // ── 2. Datos: mover el docente de Asignatura → Grupo (sin pérdida) ───────
            // 2a. Grupos ya vinculados a una asignatura heredan el docente de esa asignatura.
            migrationBuilder.Sql(@"
                UPDATE ""Grupos"" g
                SET docente_id = a.docente_id
                FROM ""Asignaturas"" a
                WHERE g.asignatura_id = a.id
                  AND g.docente_id IS NULL
                  AND a.docente_id IS NOT NULL;");

            // 2b. Toda asignatura con docente que aún no tenga un grupo con ese docente
            //     obtiene uno nuevo (así ningún docente asignado se pierde al borrar la columna).
            //     "semestre" todavía es NOT NULL en este punto (se elimina en el paso 4) — se le
            //     pone 1 aquí solo para satisfacer la restricción; la columna desaparece después.
            migrationBuilder.Sql(@"
                INSERT INTO ""Grupos"" (id, nombre, programa_id, asignatura_id, docente_id, estudiantes_inscritos, alternancia, semestre)
                SELECT gen_random_uuid(), a.nombre || ' - Grupo 1', a.programa_id, a.id, a.docente_id, 30, a.alternancia, 1
                FROM ""Asignaturas"" a
                WHERE a.docente_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM ""Grupos"" g
                      WHERE g.asignatura_id = a.id AND g.docente_id = a.docente_id
                  );");

            // ── 3. Esquema: quitar Asignaturas.docente_id (ya migrado a Grupos) ──────
            migrationBuilder.DropColumn(
                name: "docente_id",
                table: "Asignaturas");

            // ── 4. Esquema: quitar Grupos.semestre + su índice ──────────────────────
            migrationBuilder.DropIndex(
                name: "ix_grupo_semestre",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "semestre",
                table: "Grupos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaurar columnas de esquema.
            migrationBuilder.AddColumn<int>(
                name: "semestre",
                table: "Grupos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_grupo_semestre",
                table: "Grupos",
                column: "semestre");

            migrationBuilder.AddColumn<Guid>(
                name: "docente_id",
                table: "Asignaturas",
                type: "uuid",
                nullable: true);

            // Best-effort: devolver el docente del grupo a su asignatura (si hay un solo docente por asignatura).
            migrationBuilder.Sql(@"
                UPDATE ""Asignaturas"" a
                SET docente_id = g.docente_id
                FROM ""Grupos"" g
                WHERE g.asignatura_id = a.id
                  AND g.docente_id IS NOT NULL;");

            migrationBuilder.DropIndex(
                name: "ix_grupo_docente_id",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "docente_id",
                table: "Grupos");
        }
    }
}
