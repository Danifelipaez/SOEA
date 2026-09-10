using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Services;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Seeding;
using Xunit;

namespace SOEA.Tests.Infrastructure.Data
{
    /// <summary>
    /// Regresión del bug de catálogo dividido (auditoría de limpieza, hallazgo 1.1): existían dos
    /// catálogos de <c>BloqueTiempo</c> con Ids incompatibles — este seeder generaba
    /// <c>Guid.NewGuid()</c> mientras <see cref="GrillaInstitucional"/> (usada por el pipeline en
    /// memoria) genera un Id determinístico por (día, hora). Ninguna búsqueda de una sesión
    /// generada contra el catálogo sembrado en BD encontraba su bloque, así que HC-I01 (solape de
    /// docente) nunca disparaba para sesiones generadas y HC-I02 advertía siempre. Confirmado en
    /// una base de datos real: los 87 bloques sembrados no coincidían con ninguno de los 87
    /// deterministas, y 2 sesiones ya generadas apuntaban a un bloque inexistente en la tabla.
    /// Esta prueba habría fallado contra la implementación con <c>Guid.NewGuid()</c>.
    /// </summary>
    public class BloqueTiempoSeederTests
    {
        private static SOEABdContext CrearContexto()
        {
            var options = new DbContextOptionsBuilder<SOEABdContext>()
                .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
                .Options;
            var db = new SOEABdContext(options);
            db.Database.EnsureCreated();
            return db;
        }

        [Fact]
        public async Task SeedAsync_GeneraLosMismos87BloquesQueGrillaInstitucional_ConElMismoId()
        {
            using var db = CrearContexto();

            await BloqueTiempoSeeder.SeedAsync(db);

            var sembrados = await db.BloqueTiempos.ToListAsync();
            var esperados = GrillaInstitucional.GenerarBloques();

            Assert.Equal(esperados.Count, sembrados.Count);

            var idsEsperadosPorDiaHora = esperados.ToDictionary(b => (b.Dia, b.HoraInicio), b => b.Id);
            foreach (var bloque in sembrados)
            {
                Assert.True(
                    idsEsperadosPorDiaHora.TryGetValue((bloque.Dia, bloque.HoraInicio), out var idEsperado),
                    $"El bloque sembrado {bloque.Dia} {bloque.HoraInicio} no existe en GrillaInstitucional.");
                Assert.Equal(idEsperado, bloque.Id);
            }
        }

        [Fact]
        public async Task SeedAsync_EsIdempotente_NoDuplicaSiYaHayBloques()
        {
            using var db = CrearContexto();

            await BloqueTiempoSeeder.SeedAsync(db);
            var primeraCorrida = await db.BloqueTiempos.CountAsync();

            await BloqueTiempoSeeder.SeedAsync(db);
            var segundaCorrida = await db.BloqueTiempos.CountAsync();

            Assert.Equal(primeraCorrida, segundaCorrida);
        }
    }
}
