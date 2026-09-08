using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M8_UniqueEspacioBloqueSemana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales");

            // Verificación pre-deploy (2026-09-08): entornos con corridas anteriores al fix M2
            // (que ahora borra antes de insertar) acumulan filas duplicadas de
            // (espacio_id, semana, bloque_tiempo_id) — sin esta limpieza, CREATE UNIQUE INDEX
            // falla (23505) en cualquier BD con historial. Conserva una fila arbitraria por
            // grupo duplicado; todas representan la misma doble-reserva inválida.
            migrationBuilder.Sql(@"
                DELETE FROM ""AsignacionesSemanales"" a
                USING ""AsignacionesSemanales"" b
                WHERE a.espacio_id = b.espacio_id
                  AND a.semana = b.semana
                  AND a.bloque_tiempo_id = b.bloque_tiempo_id
                  AND a.espacio_id IS NOT NULL
                  AND a.id < b.id;
            ");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales",
                columns: new[] { "espacio_id", "semana", "bloque_tiempo_id" },
                unique: true,
                filter: "espacio_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_semanal_espacio_conflicto",
                table: "AsignacionesSemanales",
                columns: new[] { "espacio_id", "semana", "bloque_tiempo_id" });
        }
    }
}
