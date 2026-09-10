using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migración de DATOS (sin cambio de esquema): colapsa las dos filas semanales que el modelo
    /// anterior persistía por sesión a UNA sola, la de su semana canónica.
    ///
    /// Antes cada sesión producía una fila por semana, y para las que no alternan nada obligaba a
    /// que coincidieran: en producción 77 de 79 sesiones caían en día u hora distinta en la semana
    /// B. Eso no era una segunda vista del horario, eran dos horarios incompatibles. Ahora la
    /// franja y el aula son un dato único que aplica a todas las semanas (regla 9 / ALT-05), y solo
    /// la modalidad alterna.
    ///
    /// Se conserva la fila de la semana en la que la sesión es PRESENCIAL:
    ///   - SinAlternancia ⇒ la de semana A ("A" significa aquí "todas las semanas").
    ///   - TipoA ⇒ la de semana A · TipoB ⇒ la de semana B.
    /// La contraparte virtual de las que alternan no se guarda: no reserva aula, así que en BD era
    /// ruido. La grilla la deriva al construir el DTO.
    ///
    /// Sin esto, un horario generado ANTES de este cambio y recuperado por GET /horario/actual
    /// seguiría pintando celdas fantasma hasta la siguiente regeneración.
    /// </summary>
    public partial class M9_ColapsarFilasSemanales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AsignacionesSemanales" a
                USING "Sesiones" s
                WHERE a.sesion_id = s.id
                  AND (
                        (s.alternancia <> 'TipoB' AND a.semana = 'B')
                     OR (s.alternancia =  'TipoB' AND a.semana = 'A')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin operación a propósito: sesiones y asignaciones son datos regenerables (regla 8 de
            // CLAUDE.md — el horario se genera desde cero en cada ejecución). Reconstruir la fila
            // borrada solo devolvería el duplicado desalineado que este cambio elimina.
        }
    }
}
