using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class P2_ParejaAlternancia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pareja_alternancia_id",
                table: "Sesiones",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesion_pareja_alternancia_id",
                table: "Sesiones",
                column: "pareja_alternancia_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sesion_pareja_alternancia_id",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "pareja_alternancia_id",
                table: "Sesiones");
        }
    }
}
