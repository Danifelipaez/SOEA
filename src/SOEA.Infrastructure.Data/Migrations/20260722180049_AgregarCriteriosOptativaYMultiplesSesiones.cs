using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCriteriosOptativaYMultiplesSesiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000001"),
                column: "orden",
                value: 2);

            migrationBuilder.UpdateData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000002"),
                column: "orden",
                value: 4);

            migrationBuilder.InsertData(
                table: "CriteriosCesionAlternancia",
                columns: new[] { "id", "activo", "criterio", "orden" },
                values: new object[,]
                {
                    { new Guid("c0000000-0000-0000-0000-000000000003"), true, "Optativa", 3 },
                    { new Guid("c0000000-0000-0000-0000-000000000004"), true, "MultiplesSesiones", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000004"));

            migrationBuilder.UpdateData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000001"),
                column: "orden",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CriteriosCesionAlternancia",
                keyColumn: "id",
                keyValue: new Guid("c0000000-0000-0000-0000-000000000002"),
                column: "orden",
                value: 2);
        }
    }
}
