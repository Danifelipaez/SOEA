using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;  // EPPlus 8 license
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Docentes;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using SOEA.Infrastructure.Data;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;
using SOEA.Infrastructure.Data.Seeding;
using SOEA.Infrastructure.Excel;

ExcelPackage.License.SetNonCommercialPersonal("SOEA");

var builder = WebApplication.CreateBuilder(args);

// ── CORS (permite llamadas desde el frontend Angular en desarrollo) ────────────
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("AllowAngularDev", policy =>
        policy.WithOrigins("http://localhost:4200")
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
});
builder.Services.AddGeneticEngine();

// ── Application services ──────────────────────────────────────────────────────
// CRUD Asignaturas
builder.Services.AddScoped<CreateAsignaturaService>();
builder.Services.AddScoped<GetAsignaturaByIdService>();
builder.Services.AddScoped<GetAsignaturasService>();
builder.Services.AddScoped<DeleteAsignaturaService>();
// CRUD Docentes
builder.Services.AddScoped<DocenteService>();
// Generación de horario
builder.Services.AddScoped<GenerarHorarioService>();

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

app.UseCors("AllowAngularDev");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Endpoint de salud para readiness/liveness checks (P2.20).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();
