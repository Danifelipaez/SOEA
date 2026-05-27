using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Infrastructure.Data.Seeding
{
    /// <summary>
    /// Siembra el catálogo canónico de bloques de tiempo de 1 hora.
    /// Lunes–Viernes: 06:00–22:00. Sábado: 06:00–13:00.
    /// Operación idempotente: no hace nada si ya existen bloques.
    /// </summary>
    public static class BloqueTiempoSeeder
    {
        public static async Task SeedAsync(SOEABdContext context)
        {
            if (await context.BloqueTiempos.AnyAsync()) return;

            var bloques = new List<BloqueTiempo>();
            var diasSemana = new[]
            {
                DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles,
                DiaDeSemana.Jueves, DiaDeSemana.Viernes
            };

            foreach (var dia in diasSemana)
            {
                for (int h = 6; h < 22; h++)
                {
                    bloques.Add(new BloqueTiempo(
                        Guid.NewGuid(), dia,
                        new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
                }
            }

            // Sábado: 06:00–13:00
            for (int h = 6; h < 13; h++)
            {
                bloques.Add(new BloqueTiempo(
                    Guid.NewGuid(), DiaDeSemana.Sábado,
                    new TimeOnly(h, 0), new TimeOnly(h + 1, 0)));
            }

            await context.BloqueTiempos.AddRangeAsync(bloques);
            await context.SaveChangesAsync();
        }
    }
}
