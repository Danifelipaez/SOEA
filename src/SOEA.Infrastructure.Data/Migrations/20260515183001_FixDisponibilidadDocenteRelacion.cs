using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixDisponibilidadDocenteRelacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BloqueTiempos_Docentes_DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.DropIndex(
                name: "IX_BloqueTiempos_DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.DropColumn(
                name: "DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.AddColumn<string>(
                name: "MotivoConflicto",
                table: "Sesiones",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DisponibilidadDocente",
                columns: table => new
                {
                    BloqueTiempoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocenteId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisponibilidadDocente", x => new { x.BloqueTiempoId, x.DocenteId });
                    table.ForeignKey(
                        name: "FK_DisponibilidadDocente_BloqueTiempos_BloqueTiempoId",
                        column: x => x.BloqueTiempoId,
                        principalTable: "BloqueTiempos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DisponibilidadDocente_Docentes_DocenteId",
                        column: x => x.DocenteId,
                        principalTable: "Docentes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisponibilidadDocente_DocenteId",
                table: "DisponibilidadDocente",
                column: "DocenteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisponibilidadDocente");

            migrationBuilder.DropColumn(
                name: "MotivoConflicto",
                table: "Sesiones");

            migrationBuilder.AddColumn<Guid>(
                name: "DocenteId",
                table: "BloqueTiempos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BloqueTiempos_DocenteId",
                table: "BloqueTiempos",
                column: "DocenteId");

            migrationBuilder.AddForeignKey(
                name: "FK_BloqueTiempos_Docentes_DocenteId",
                table: "BloqueTiempos",
                column: "DocenteId",
                principalTable: "Docentes",
                principalColumn: "id");
        }
    }
}
