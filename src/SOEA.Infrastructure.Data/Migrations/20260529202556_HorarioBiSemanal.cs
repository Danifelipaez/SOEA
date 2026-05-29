using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HorarioBiSemanal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AsignacionesSemanales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semana = table.Column<string>(type: "text", nullable: false),
                    bloque_tiempo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    espacio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modalidad = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsignacionesSemanales", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales",
                columns: new[] { "espacio_id", "semana", "bloque_tiempo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_semanal_sesion_id",
                table: "AsignacionesSemanales",
                column: "sesion_id");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_semanal_sesion_semana",
                table: "AsignacionesSemanales",
                columns: new[] { "sesion_id", "semana" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsignacionesSemanales");
        }
    }
}
