using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SOEA.Infrastructure.Data.Context;

namespace SOEA.Tests.Integracion
{
    /// <summary>
    /// PostgreSQL real para las pruebas de integración. EF InMemory y los fakes no aplican FKs ni
    /// índices únicos, así que ninguno de los bloqueantes P0 de la auditoría 2026-09-14 aparecía en
    /// la suite. Cada corrida usa una BD nueva y desechable.
    /// Servidor y credenciales: variable SOEA_TEST_DB o, en local, la cadena de
    /// src/SOEA.API/appsettings.Development.json (de ella solo se toma el servidor; nunca se toca
    /// la BD que nombra). Sin servidor alcanzable las pruebas se omiten en vez de fallar.
    /// ponytail: sin Testcontainers — no hay Docker en la máquina de desarrollo; si CI lo necesita,
    /// basta un service container de postgres y SOEA_TEST_DB.
    /// </summary>
    internal static class PostgresPruebas
    {
        private static readonly Lazy<string?> CadenaServidor = new(Resolver);

        public static bool Disponible => CadenaServidor.Value is not null;

        public static string NuevaBd() =>
            new NpgsqlConnectionStringBuilder(CadenaServidor.Value) { Database = $"soea_it_{Guid.NewGuid():N}" }.ConnectionString;

        public static SOEABdContext Contexto(string cadena) =>
            new(new DbContextOptionsBuilder<SOEABdContext>().UseNpgsql(cadena).Options);

        public static async Task BorrarAsync(string cadena)
        {
            NpgsqlConnection.ClearAllPools(); // el pool del API mantiene conexiones abiertas a la BD
            await using var db = Contexto(cadena);
            await db.Database.EnsureDeletedAsync();
        }

        private static string? Resolver()
        {
            var cadena = Environment.GetEnvironmentVariable("SOEA_TEST_DB") ?? CadenaDelApi();
            if (string.IsNullOrWhiteSpace(cadena)) return null;
            try
            {
                using var cn = new NpgsqlConnection(
                    new NpgsqlConnectionStringBuilder(cadena) { Database = "postgres", Timeout = 3 }.ConnectionString);
                cn.Open();
                return cadena;
            }
            catch (Exception) { return null; }
        }

        private static string? CadenaDelApi()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var ruta = Path.Combine(dir.FullName, "src", "SOEA.API", "appsettings.Development.json");
                if (!File.Exists(ruta)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(ruta),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                return doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                       cs.TryGetProperty("DefaultConnection", out var dc) ? dc.GetString() : null;
            }
            return null;
        }
    }

    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (!PostgresPruebas.Disponible)
                Skip = "Sin PostgreSQL de pruebas: defina SOEA_TEST_DB o appsettings.Development.json del API.";
        }
    }

    /// <summary>
    /// El API completo (Program.cs real: DI, migraciones al arrancar, seed de bloques, manejo de
    /// errores) sobre una BD nueva, compartido por toda la colección "Postgres".
    /// </summary>
    public sealed class ApiPostgresFixture : IAsyncLifetime
    {
        private string _cadena = string.Empty;
        private WebApplicationFactory<Program>? _factory;

        public HttpClient Client { get; private set; } = null!;

        public SOEABdContext Db() => PostgresPruebas.Contexto(_cadena);

        public Task InitializeAsync()
        {
            if (!PostgresPruebas.Disponible) return Task.CompletedTask;

            _cadena = PostgresPruebas.NuevaBd();
            // Variable de entorno y no UseSetting: Program.cs lee la cadena antes de Build(), y las
            // variables de entorno ganan sobre cualquier appsettings*.json.
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _cadena);
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
            Client = _factory.CreateClient(); // arranca el host: Migrate() + seed sobre la BD nueva
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_factory is null) return;
            Client.Dispose();
            await _factory.DisposeAsync();
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            await PostgresPruebas.BorrarAsync(_cadena);
        }
    }

    [CollectionDefinition("Postgres")]
    public sealed class PostgresCollection : ICollectionFixture<ApiPostgresFixture> { }
}
