using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAsignaturaLogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requiere_lab",
                table: "Asignaturas");

            migrationBuilder.RenameColumn(
                name: "bloques_semanales",
                table: "Asignaturas",
                newName: "sesiones_por_semana");

            migrationBuilder.AddColumn<int>(
                name: "horas_por_sesion",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sesiones_laboratorio_semestre",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "horas_por_sesion",
                table: "Asignaturas");

            migrationBuilder.DropColumn(
                name: "sesiones_laboratorio_semestre",
                table: "Asignaturas");

            migrationBuilder.RenameColumn(
                name: "sesiones_por_semana",
                table: "Asignaturas",
                newName: "bloques_semanales");

            migrationBuilder.AddColumn<bool>(
                name: "requiere_lab",
                table: "Asignaturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
