using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class P1_GrupoComoEje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Columna nueva e independiente de "disponibilidad" (P1.2/A2) — no es su sustituta,
            // sólo comparten la misma estrategia de JSON-en-texto.
            migrationBuilder.AddColumn<string>(
                name: "requisitos_espacio",
                table: "Grupos",
                type: "text",
                nullable: true);

            // Copia Asignaturas.espacio_fijo_id como requisito de laboratorio a cada grupo de esa
            // asignatura, ANTES de borrar la columna (decisión: el requisito de espacio vive en
            // Grupo, no en Asignatura). TipoSesion.Laboratorio=2, TipoEspacio.Laboratorio=1 — System.Text.Json
            // serializa enums como su valor numérico con las opciones por defecto que usa la conversión EF.
            migrationBuilder.Sql(@"
                UPDATE ""Grupos"" g
                SET requisitos_espacio = json_build_array(
                    json_build_object(
                        'TipoSesion', 2,
                        'EspacioId', a.espacio_fijo_id,
                        'TipoEspacio', 1,
                        'Sesiones', 0
                    )
                )::text
                FROM ""Asignaturas"" a
                WHERE g.asignatura_id = a.id AND a.espacio_fijo_id IS NOT NULL;
            ");

            // "disponibilidad" (List<FranjaHoraria>) nunca llegaba a persistirse realmente (el
            // controller sólo escribía disponibilidad_ui_json) — eliminación, no sustitución.
            migrationBuilder.DropColumn(
                name: "disponibilidad",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "espacio_fijo_id",
                table: "Asignaturas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disponibilidad",
                table: "Grupos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "espacio_fijo_id",
                table: "Asignaturas",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "requisitos_espacio",
                table: "Grupos");
        }
    }
}
