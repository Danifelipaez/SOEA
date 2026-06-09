using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SOEA.Infrastructure.Data.Context
{
    /// <summary>
    /// Fábrica de contexto para migraciones en tiempo de diseño.
    /// La cadena de conexión NO se hardcodea (P0.1 auditoría): se resuelve en este orden:
    ///   1. Variable de entorno SOEA_DESIGN_TIME_DB.
    ///   2. appsettings.Development.json del proyecto API (gitignored).
    ///   3. appsettings.json del proyecto API.
    /// Si ninguna está disponible se lanza un error explícito.
    /// </summary>
    public class SOEABdContextFactory : IDesignTimeDbContextFactory<SOEABdContext>
    {
        public SOEABdContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SOEABdContext>();
            optionsBuilder.UseNpgsql(ResolverCadenaConexion());

            return new SOEABdContext(optionsBuilder.Options);
        }

        private static string ResolverCadenaConexion()
        {
            var desdeEnv = Environment.GetEnvironmentVariable("SOEA_DESIGN_TIME_DB");
            if (!string.IsNullOrWhiteSpace(desdeEnv))
                return desdeEnv;

            // El comando `dotnet ef ... --startup-project ../SOEA.API` ejecuta desde
            // SOEA.Infrastructure.Data, por lo que buscamos el appsettings del API relativo.
            var rutasApi = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "..", "SOEA.API"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "SOEA.API"),
                Directory.GetCurrentDirectory()
            };

            foreach (var basePath in rutasApi)
            {
                if (!Directory.Exists(basePath)) continue;

                var config = new ConfigurationBuilder()
                    .SetBasePath(Path.GetFullPath(basePath))
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .AddEnvironmentVariables()
                    .Build();

                var cadena = config.GetConnectionString("DefaultConnection");
                if (!string.IsNullOrWhiteSpace(cadena))
                    return cadena;
            }

            throw new InvalidOperationException(
                "No se encontró la cadena de conexión para migraciones. Defina la variable de entorno " +
                "SOEA_DESIGN_TIME_DB o configure ConnectionStrings:DefaultConnection en " +
                "src/SOEA.API/appsettings.Development.json.");
        }
    }
}
