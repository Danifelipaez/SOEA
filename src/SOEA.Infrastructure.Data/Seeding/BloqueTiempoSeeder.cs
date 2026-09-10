using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Services;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Seeding
{
    /// <summary>
    /// Siembra el catálogo canónico de bloques de tiempo de 1 hora.
    /// Fuente única de la grilla: <see cref="GrillaInstitucional"/> — antes este seeder generaba
    /// los mismos 87 bloques (día, hora) pero con <c>Guid.NewGuid()</c>, mientras que
    /// GenerarHorarioService/ReacomodarHorarioService/ValidadorRestriccionesDuras regeneran la
    /// grilla en memoria con el Id determinístico de GrillaInstitucional. Dos catálogos con Ids
    /// incompatibles para el mismo bloque significaba que ningún <c>TryGetValue</c> contra la BD
    /// (AsignarDocenteSesionService, CrearSesionManualService) encontraba nunca el bloque de una
    /// sesión generada por el pipeline — HC-I01 (solape de docente) nunca disparaba para esas
    /// sesiones, y HC-S01/HC-SEP en creación manual quedaban vacías. Confirmado en la BD de
    /// desarrollo: los 87 bloques sembrados no coincidían con ninguno de los 87 que genera
    /// GrillaInstitucional, y 2 sesiones ya reales tenían un BloqueTiempoId que no existía en
    /// esta tabla.
    /// Una base ya sembrada (Ids aleatorios) necesita además la migración de datos
    /// M10_UnificarCatalogoBloques — este seeder por sí solo solo corrige bases nuevas.
    /// Operación idempotente: no hace nada si ya existen bloques.
    /// </summary>
    public static class BloqueTiempoSeeder
    {
        public static async Task SeedAsync(SOEABdContext context)
        {
            if (await context.BloqueTiempos.AnyAsync()) return;

            await context.BloqueTiempos.AddRangeAsync(GrillaInstitucional.GenerarBloques());
            await context.SaveChangesAsync();
        }
    }
}
