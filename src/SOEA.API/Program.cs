using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;  // EPPlus 8 license
using SOEA.API;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.CriteriosCesionAlternancia;
using SOEA.Application.Features.Docentes;
using SOEA.Application.Features.Espacios;
using SOEA.Application.Features.Grupos;
using SOEA.Application.Features.Horario;
using SOEA.Application.Features.Sesiones;
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
// B5 auditoría: sin CommandTimeout, una migración lenta en Database.Migrate() al arrancar (o una
// consulta puntual pesada) usaba el default de Npgsql (30s) sin poder ajustarse.
// ponytail: NO se activa EnableRetryOnFailure — UnitOfWork (BeginTransactionAsync/CommitAsync
// manual, usado por GenerarHorarioService/ImportarCurriculumService/ReacomodarHorarioService) usa
// transacciones iniciadas por el usuario, incompatibles con la execution strategy de reintento de
// EF Core a menos que se envuelvan en Database.CreateExecutionStrategy().ExecuteAsync(...) — de
// otro modo el primer BeginTransactionAsync() con retry activo lanza InvalidOperationException en
// los 3 flujos de escritura críticos. Upgrade path: refactorizar IUnitOfWork a un único
// ExecuteInTransactionAsync(Func<Task>) que use la execution strategy, y entonces sí activar retry.
builder.Services.AddDbContext<SOEABdContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(60)));

// ── Repositorios ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
builder.Services.AddScoped<IHorarioRepositorio, HorarioRepositorio>();
builder.Services.AddScoped<ISesionRepositorio, SesionRepositorio>();
builder.Services.AddScoped<IAsignacionSemanalRepositorio, AsignacionSemanalRepositorio>();
builder.Services.AddScoped<IDocenteRepositorio, DocenteRepositorio>();
builder.Services.AddScoped<IEspacioRepositorio, EspacioRepositorio>();
builder.Services.AddScoped<IGrupoRepositorio, GrupoRepositorio>();
builder.Services.AddScoped<IBloqueTiempoRepositorio, BloqueTiempoRepositorio>();
builder.Services.AddScoped<ICriterioCesionAlternanciaRepositorio, CriterioCesionAlternanciaRepositorio>();
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
    opts.ExportarModelo     = builder.Configuration.GetValue<bool>("CpSat:ExportarModelo");
    opts.TimeoutSegundos    = builder.Configuration.GetValue("CpSat:TimeoutSegundos", 120);
    opts.NumWorkers         = builder.Configuration.GetValue("CpSat:NumWorkers", 0);
    // M3 auditoría: SweepGrupos (diagnóstico de qué grupo causa infactibilidad) ya estaba
    // implementado y testeado pero nunca se leía de config, así que nunca se activaba.
    opts.SweepGrupos        = builder.Configuration.GetValue<bool>("CpSat:SweepGrupos");
    opts.SweepGruposMaximo  = builder.Configuration.GetValue("CpSat:SweepGruposMaximo", 20);
});
builder.Services.AddGeneticEngine();

// ── Application services ──────────────────────────────────────────────────────
// Purga en cascada de sesiones generadas al borrar catálogo (Asignatura/Grupo/Espacio)
builder.Services.AddScoped<SesionCascadeService>();
// CRUD Asignaturas
builder.Services.AddScoped<AsignaturaService>();
builder.Services.AddScoped<CrearSesionManualService>();
// CRUD Grupos
builder.Services.AddScoped<GrupoService>();
// CRUD Docentes
builder.Services.AddScoped<DocenteService>();
builder.Services.AddScoped<FusionDocentesService>();
// CRUD Espacios
builder.Services.AddScoped<EspacioService>();
// Lista ordenada/activable de criterios de cesión a alternancia (cesión por saturación de espacio)
builder.Services.AddScoped<CriterioCesionAlternanciaService>();
// Generación de horario
builder.Services.AddScoped<GenerarHorarioService>();
// Importación de curriculum
builder.Services.AddScoped<ImportarCurriculumService>();
// Asignación de docente post-generación (HU-04, Etapa 4)
builder.Services.AddScoped<AsignarDocenteSesionService>();
// Petición 13: recálculo mínimo al editar una sesión ya generada
builder.Services.AddScoped<ReacomodarHorarioService>();

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
// GlobalExceptionHandler traduce antes las excepciones de dominio/EF Core conocidas (auditoría
// e2e pre-producción: varios controllers nunca capturaban DbUpdateException en Delete y
// escalaban a un 500 sin causa real).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
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
