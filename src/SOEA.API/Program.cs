using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;  // EPPlus 8 license
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Docentes;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.TiposAlternancia;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using SOEA.Application.Features.Import;
using SOEA.Infrastructure.Data;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;
using SOEA.Infrastructure.Data.Seeding;
using SOEA.Infrastructure.Excel;

ExcelPackage.License.SetNonCommercialPersonal("SOEA");

var builder = WebApplication.CreateBuilder(args);

// ── CORS ──────────────────────────────────────────────────────────────────────
// En desarrollo: http://localhost:4200
// En producción: la URL del Static Web App se pone en AllowedOrigins (App Service config)
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:4200"];

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── Base de datos (PostgreSQL) ────────────────────────────────────────────────
// La cadena vive en appsettings.Development.json (gitignored) o en variables de
// entorno / user-secrets en otros ambientes — nunca commiteada (P0.1 auditoría).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No hay cadena de conexión configurada. Defina ConnectionStrings:DefaultConnection en " +
        "appsettings.Development.json (desarrollo) o en variables de entorno / user-secrets.");
}
builder.Services.AddDbContext<SOEABdContext>(options =>
    options.UseNpgsql(connectionString));

// ── Repositorios ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
builder.Services.AddScoped<IHorarioRepositorio, HorarioRepositorio>();
builder.Services.AddScoped<ISesionRepositorio, SesionRepositorio>();
builder.Services.AddScoped<IAsignacionSemanalRepositorio, AsignacionSemanalRepositorio>();
builder.Services.AddScoped<IDocenteRepositorio, DocenteRepositorio>();
builder.Services.AddScoped<IEspacioRepositorio, EspacioRepositorio>();
builder.Services.AddScoped<IGrupoRepositorio, GrupoRepositorio>();
builder.Services.AddScoped<IBloqueTiempoRepositorio, BloqueTiempoRepositorio>();
builder.Services.AddScoped<ITipoAlternanciaConfigRepositorio, TipoAlternanciaConfigRepositorio>();
builder.Services.AddScoped<IFacultadRepositorio, FacultadRepositorio>();
builder.Services.AddScoped<IProgramaRepositorio, ProgramaRepositorio>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Excel reader ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<ILectorExcel, LectorExcel>();

// ── Motores de scheduling ─────────────────────────────────────────────────────
builder.Services.AddGraphColoringEngine();
// El volcado de cp_model_debug.txt a disco solo se habilita explícitamente vía
// CpSat:ExportarModelo (default false). Evita llenar el disco y filtrar el modelo
// con IDs de docentes/sesiones en producción (P0.2 auditoría).
builder.Services.AddConstraintProgEngine(opts =>
{
    opts.ExportarModelo  = builder.Configuration.GetValue<bool>("CpSat:ExportarModelo");
    opts.TimeoutSegundos = builder.Configuration.GetValue("CpSat:TimeoutSegundos", 120);
    opts.NumWorkers      = builder.Configuration.GetValue("CpSat:NumWorkers", 0);
});
builder.Services.AddGeneticEngine();

// ── Application services ──────────────────────────────────────────────────────
// CRUD Asignaturas
builder.Services.AddScoped<AsignaturaService>();
builder.Services.AddScoped<CrearSesionManualService>();
// CRUD Docentes
builder.Services.AddScoped<DocenteService>();
builder.Services.AddScoped<FusionDocentesService>();
// Catálogo de tipos de alternancia (Inc. C)
builder.Services.AddScoped<TipoAlternanciaConfigService>();
// Generación de horario
builder.Services.AddScoped<GenerarHorarioService>();
// Importación de curriculum
builder.Services.AddScoped<ImportarCurriculumService>();
// Asignación de docente post-generación (HU-04, Etapa 4)
builder.Services.AddScoped<AsignarDocenteSesionService>();

// ── OpenAPI + Controladores ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ── Manejo global de excepciones (ProblemDetails) ─────────────────────────────
// Toda excepción no controlada se devuelve como application/problem+json con traceId,
// en lugar de un 500 con formato inconsistente (P1.4 auditoría).
builder.Services.AddProblemDetails();

var app = builder.Build();

// El manejador de excepciones debe ir lo más temprano posible en el pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

// ── Migraciones automáticas + seed del catálogo de bloques ───────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SOEABdContext>();
    db.Database.Migrate();
    await BloqueTiempoSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Endpoint de salud para readiness/liveness checks (P2.20).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();
