using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFacultadPrograma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asignaturas_codigo",
                table: "Asignaturas");

            migrationBuilder.AddColumn<Guid>(
                name: "DocenteId",
                table: "BloqueTiempos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Facultades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facultades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Programas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    facultad_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Programas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloqueTiempos_DocenteId",
                table: "BloqueTiempos",
                column: "DocenteId");

            migrationBuilder.CreateIndex(
                name: "ix_asignaturas_codigo_programa",
                table: "Asignaturas",
                columns: new[] { "codigo", "programa_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_programas_facultad_id",
                table: "Programas",
                column: "facultad_id");

            migrationBuilder.AddForeignKey(
                name: "FK_BloqueTiempos_Docentes_DocenteId",
                table: "BloqueTiempos",
                column: "DocenteId",
                principalTable: "Docentes",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BloqueTiempos_Docentes_DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.DropTable(
                name: "Facultades");

            migrationBuilder.DropTable(
                name: "Programas");

            migrationBuilder.DropIndex(
                name: "IX_BloqueTiempos_DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.DropIndex(
                name: "ix_asignaturas_codigo_programa",
                table: "Asignaturas");

            migrationBuilder.DropColumn(
                name: "DocenteId",
                table: "BloqueTiempos");

            migrationBuilder.CreateIndex(
                name: "ix_asignaturas_codigo",
                table: "Asignaturas",
                column: "codigo",
                unique: true);
        }
    }
}
