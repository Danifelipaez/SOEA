using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EtapaInicialPresencialFirst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bloqueada",
                table: "Sesiones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "patron_alternancia_id",
                table: "Sesiones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_flujo",
                table: "Sesiones",
                type: "text",
                nullable: false,
                defaultValue: "Laboratorio");

            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "Asignaturas",
                type: "text",
                nullable: false,
                defaultValue: "Obligatoria");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_fin_max",
                table: "Asignaturas",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_inicio_min",
                table: "Asignaturas",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesion_patron_alternancia_id",
                table: "Sesiones",
                column: "patron_alternancia_id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_TiposAlternancia_patron_alternancia_id",
                table: "Sesiones",
                column: "patron_alternancia_id",
                principalTable: "TiposAlternancia",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_TiposAlternancia_patron_alternancia_id",
                table: "Sesiones");

            migrationBuilder.DropIndex(
                name: "ix_sesion_patron_alternancia_id",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "bloqueada",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "patron_alternancia_id",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "tipo_flujo",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "Asignaturas");

            migrationBuilder.DropColumn(
                name: "hora_fin_max",
                table: "Asignaturas");

            migrationBuilder.DropColumn(
                name: "hora_inicio_min",
                table: "Asignaturas");
        }
    }
}
