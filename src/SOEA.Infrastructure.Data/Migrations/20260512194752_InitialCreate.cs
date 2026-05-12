using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Asignaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bloques_semanales = table.Column<int>(type: "integer", nullable: false),
                    requiere_lab = table.Column<bool>(type: "boolean", nullable: false),
                    alternancia = table.Column<string>(type: "text", nullable: false),
                    programa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asignaturas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "BloqueTiempos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia = table.Column<string>(type: "text", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloqueTiempos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Docentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    apellido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    maximo_horas_semanales = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    disponibilidad = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Docentes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Espacios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    capacidad = table.Column<int>(type: "integer", nullable: false),
                    edificio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    piso = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Espacios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Grupos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    programa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semestre = table.Column<int>(type: "integer", nullable: false),
                    estudiantes_inscritos = table.Column<int>(type: "integer", nullable: false),
                    alternancia = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grupos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Horarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    semestre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    sesion_ids = table.Column<string>(type: "text", nullable: false),
                    hard_constraint_violations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    soft_constraint_fitness_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false, defaultValue: 0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Horarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Sesiones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    docente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bloque_tiempo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    espacio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grupo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alternancia = table.Column<string>(type: "text", nullable: false),
                    modalidad = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    duracion_horas = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    es_bloque = table.Column<bool>(type: "boolean", nullable: false),
                    esta_dividida = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sesiones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignaturas_codigo",
                table: "Asignaturas",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asignaturas_programa_id",
                table: "Asignaturas",
                column: "programa_id");

            migrationBuilder.CreateIndex(
                name: "ix_bloque_tiempo_dia",
                table: "BloqueTiempos",
                column: "dia");

            migrationBuilder.CreateIndex(
                name: "ix_docentes_correo",
                table: "Docentes",
                column: "correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_espacios_tipo",
                table: "Espacios",
                column: "tipo");

            migrationBuilder.CreateIndex(
                name: "ix_grupo_nombre",
                table: "Grupos",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_grupo_programa_id",
                table: "Grupos",
                column: "programa_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupo_semestre",
                table: "Grupos",
                column: "semestre");

            migrationBuilder.CreateIndex(
                name: "ix_horario_estado",
                table: "Horarios",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_horario_generated_at",
                table: "Horarios",
                column: "generated_at");

            migrationBuilder.CreateIndex(
                name: "ix_horario_semestre",
                table: "Horarios",
                column: "semestre");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_asignatura_id",
                table: "Sesiones",
                column: "asignatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_bloque_tiempo_id",
                table: "Sesiones",
                column: "bloque_tiempo_id");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_docente_bloque_conflicto",
                table: "Sesiones",
                columns: new[] { "docente_id", "bloque_tiempo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sesion_docente_id",
                table: "Sesiones",
                column: "docente_id");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_espacio_bloque_conflicto",
                table: "Sesiones",
                columns: new[] { "espacio_id", "bloque_tiempo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sesion_espacio_id",
                table: "Sesiones",
                column: "espacio_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Asignaturas");

            migrationBuilder.DropTable(
                name: "BloqueTiempos");

            migrationBuilder.DropTable(
                name: "Docentes");

            migrationBuilder.DropTable(
                name: "Espacios");

            migrationBuilder.DropTable(
                name: "Grupos");

            migrationBuilder.DropTable(
                name: "Horarios");

            migrationBuilder.DropTable(
                name: "Sesiones");
        }
    }
}
