using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CesionAlternanciaConfigurable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cedida_por_saturacion",
                table: "Sesiones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "es_candidata_alternancia",
                table: "Asignaturas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CriteriosCesionAlternancia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    criterio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriteriosCesionAlternancia", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "CriteriosCesionAlternancia",
                columns: new[] { "id", "activo", "criterio", "orden" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000001"), true, "Electiva", 1 },
                    { new Guid("c0000000-0000-0000-0000-000000000002"), true, "Elegible", 2 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriteriosCesionAlternancia");

            migrationBuilder.DropColumn(
                name: "cedida_por_saturacion",
                table: "Sesiones");

            migrationBuilder.DropColumn(
                name: "es_candidata_alternancia",
                table: "Asignaturas");
        }
    }
}
