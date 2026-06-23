using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Etapa5GrupoConDisponibilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "asignatura_id",
                table: "Grupos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo",
                table: "Grupos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disponibilidad",
                table: "Grupos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disponibilidad_ui_json",
                table: "Grupos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "facultad_id",
                table: "Grupos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_grupo_asignatura_id",
                table: "Grupos",
                column: "asignatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupo_codigo",
                table: "Grupos",
                column: "codigo",
                unique: true,
                filter: "codigo IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_grupo_asignatura_id",
                table: "Grupos");

            migrationBuilder.DropIndex(
                name: "ix_grupo_codigo",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "asignatura_id",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "codigo",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "disponibilidad",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "disponibilidad_ui_json",
                table: "Grupos");

            migrationBuilder.DropColumn(
                name: "facultad_id",
                table: "Grupos");
        }
    }
}
