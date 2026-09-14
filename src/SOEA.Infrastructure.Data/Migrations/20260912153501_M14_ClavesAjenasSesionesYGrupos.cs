using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M14_ClavesAjenasSesionesYGrupos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sesiones_grupo_id",
                table: "Sesiones",
                column: "grupo_id");

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos",
                column: "facultad_id");

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Asignaturas_asignatura_id",
                table: "Grupos",
                column: "asignatura_id",
                principalTable: "Asignaturas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Docentes_docente_id",
                table: "Grupos",
                column: "docente_id",
                principalTable: "Docentes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Facultades_facultad_id",
                table: "Grupos",
                column: "facultad_id",
                principalTable: "Facultades",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Grupos_Programas_programa_id",
                table: "Grupos",
                column: "programa_id",
                principalTable: "Programas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Asignaturas_asignatura_id",
                table: "Sesiones",
                column: "asignatura_id",
                principalTable: "Asignaturas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_BloqueTiempos_bloque_tiempo_id",
                table: "Sesiones",
                column: "bloque_tiempo_id",
                principalTable: "BloqueTiempos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Docentes_docente_id",
                table: "Sesiones",
                column: "docente_id",
                principalTable: "Docentes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Espacios_espacio_id",
                table: "Sesiones",
                column: "espacio_id",
                principalTable: "Espacios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sesiones_Grupos_grupo_id",
                table: "Sesiones",
                column: "grupo_id",
                principalTable: "Grupos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Asignaturas_asignatura_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Docentes_docente_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Facultades_facultad_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Grupos_Programas_programa_id",
                table: "Grupos");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Asignaturas_asignatura_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_BloqueTiempos_bloque_tiempo_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Docentes_docente_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Espacios_espacio_id",
                table: "Sesiones");

            migrationBuilder.DropForeignKey(
                name: "FK_Sesiones_Grupos_grupo_id",
                table: "Sesiones");

            migrationBuilder.DropIndex(
                name: "IX_Sesiones_grupo_id",
                table: "Sesiones");

            migrationBuilder.DropIndex(
                name: "IX_Grupos_facultad_id",
                table: "Grupos");
        }
    }
}
