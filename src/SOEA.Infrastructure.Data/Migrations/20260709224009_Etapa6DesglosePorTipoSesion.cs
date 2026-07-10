using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Etapa6DesglosePorTipoSesion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTA: el diff automático de EF detecta esto como un rename (horas_por_sesion →
            // sesiones_teoria_presencial_semana, sesiones_por_semana → sesiones_teoria_virtual_semana),
            // lo cual mezclaría horas con conteos y reclasificaría teoría presencial como virtual.
            // Se hace a mano: agregar las 6 columnas nuevas, hacer backfill desde las 2 viejas
            // (todo lo existente era teoría presencial, según el shape legado) y luego borrarlas.
            migrationBuilder.AddColumn<int>(
                name: "sesiones_teoria_presencial_semana",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "horas_teoria_presencial",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sesiones_teoria_virtual_semana",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "horas_teoria_virtual",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sesiones_laboratorio_semana",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "horas_laboratorio",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                @"UPDATE ""Asignaturas""
                  SET sesiones_teoria_presencial_semana = sesiones_por_semana,
                      horas_teoria_presencial = horas_por_sesion;");

            migrationBuilder.DropColumn(name: "horas_por_sesion", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "sesiones_por_semana", table: "Asignaturas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "horas_por_sesion",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sesiones_por_semana",
                table: "Asignaturas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                @"UPDATE ""Asignaturas""
                  SET sesiones_por_semana = sesiones_teoria_presencial_semana,
                      horas_por_sesion = horas_teoria_presencial;");

            migrationBuilder.DropColumn(name: "sesiones_teoria_presencial_semana", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "horas_teoria_presencial", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "sesiones_teoria_virtual_semana", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "horas_teoria_virtual", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "sesiones_laboratorio_semana", table: "Asignaturas");
            migrationBuilder.DropColumn(name: "horas_laboratorio", table: "Asignaturas");
        }
    }
}
