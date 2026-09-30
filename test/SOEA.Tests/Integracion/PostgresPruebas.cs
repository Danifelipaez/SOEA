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
    /// la BD que nombra). Sin nada configurado (ni variable ni appsettings) o con el servidor de appsettings
    /// inalcanzable las pruebas se omiten, para no estorbar en una máquina sin Postgres.
    /// NEW-9 auditoría 2026-09-28: si SOEA_TEST_DB está definida es una declaración de intención (CI, o
    /// alguien que quiere correr estas pruebas) y un servidor inalcanzable FALLA con un mensaje claro en vez de
    /// omitir: antes un arranque lento (timeout de 3 s) convertía en silencio las pruebas en "Omitido" y la
    /// suite salía en verde sin haber probado nada contra la BD.
    /// ponytail: sin Testcontainers — no hay Docker en la máquina de desarrollo; CI usa un service container de
    /// postgres (ver .github/workflows/ci.yml) y SOEA_TEST_DB.
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
            var explicita = Environment.GetEnvironmentVariable("SOEA_TEST_DB");
            var cadena = string.IsNullOrWhiteSpace(explicita) ? CadenaDelApi() : explicita;
            if (string.IsNullOrWhiteSpace(cadena)) return null;

            // Con variable explícita: hasta 5 intentos de 10 s (un servidor que está arrancando tarda), y si
            // ninguno conecta se lanza. Sin ella (appsettings de desarrollo): un solo intento corto y se omite.
            var exigida = !string.IsNullOrWhiteSpace(explicita);
            var intentos = exigida ? 5 : 1;
            Exception? ultimo = null;
            for (var i = 0; i < intentos; i++)
            {
                try
                {
                    using var cn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cadena)
                        { Database = "postgres", Timeout = exigida ? 10 : 3 }.ConnectionString);
                    cn.Open();
                    return cadena;
                }
                catch (Exception ex) { ultimo = ex; if (exigida && i < intentos - 1) Thread.Sleep(2000); }
            }
            return exigida
                ? throw new InvalidOperationException(
                    "SOEA_TEST_DB está definida pero no se pudo conectar al servidor de PostgreSQL (" + ultimo?.GetType().Name +
                    "). Las pruebas de integración NO se omiten en este caso: arranque el servidor o quite la variable.", ultimo)
                : null;
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
