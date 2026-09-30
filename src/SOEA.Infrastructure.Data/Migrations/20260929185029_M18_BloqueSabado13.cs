using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migración de DATOS (sin cambio de esquema): agrega el bloque Sábado 13:00–14:00 que faltaba.
    ///
    /// M10 sembró 87 bloques (sábado hasta las 13:00), pero <c>GrillaInstitucional</c> se amplió
    /// después a cierre de sábado 14:00 → 88 bloques. Cuando el solver ubicaba una sesión de 1 h que
    /// empezaba Sáb 13:00, el INSERT en <c>Sesiones</c> violaba
    /// <c>FK_Sesiones_BloqueTiempos_bloque_tiempo_id</c> y el API respondía un 409 genérico después de
    /// minutos de solver. Idempotente: no hace nada si el bloque ya existe.
    /// </summary>
    public partial class M18_BloqueSabado13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Id = MD5("bloque-Sábado-13:00") como Guid, igual que GrillaInstitucional.IdDeterministico.
            migrationBuilder.Sql("""
                INSERT INTO "BloqueTiempos" (id, dia, hora_inicio, hora_fin)
                SELECT '75149140-a4aa-5d67-edab-0fdcbc3af399', 'Sábado', '13:00:00'::time, '14:00:00'::time
                WHERE NOT EXISTS (SELECT 1 FROM "BloqueTiempos" WHERE id = '75149140-a4aa-5d67-edab-0fdcbc3af399');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin operación: quitar el bloque volvería a romper cualquier sesión que ya lo use.
        }
    }
}
