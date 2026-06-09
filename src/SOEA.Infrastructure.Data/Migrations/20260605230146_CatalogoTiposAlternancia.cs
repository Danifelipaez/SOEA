using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoTiposAlternancia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TiposAlternancia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    patron_base = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    semanas_presenciales = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    es_sistema = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposAlternancia", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "TiposAlternancia",
                columns: new[] { "id", "activo", "color", "es_sistema", "nombre", "patron_base", "semanas_presenciales" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), true, "#1565c0", true, "Tipo A", "PresencialEnSemanaA", 8 },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), true, "#e65100", true, "Tipo B", "PresencialEnSemanaB", 8 },
                    { new Guid("a0000000-0000-0000-0000-000000000003"), true, "#607d8b", true, "Sin alternancia", "SinAlternancia", 16 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TiposAlternancia");
        }
    }
}
