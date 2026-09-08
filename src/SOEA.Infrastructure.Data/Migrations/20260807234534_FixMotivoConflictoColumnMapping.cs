using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixMotivoConflictoColumnMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MotivoConflicto",
                table: "Sesiones",
                newName: "motivo_conflicto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "motivo_conflicto",
                table: "Sesiones",
                newName: "MotivoConflicto");
        }
    }
}
