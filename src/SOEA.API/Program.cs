using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;  // EPPlus 8 license
using SOEA.Application.Features.Asignaturas;
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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<SOEABdContext>(options =>
    options.UseNpgsql(connectionString));

// ── Repositorios ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
builder.Services.AddScoped<IHorarioRepositorio, HorarioRepositorio>();
builder.Services.AddScoped<ISesionRepositorio, SesionRepositorio>();
builder.Services.AddScoped<IDocenteRepositorio, DocenteRepositorio>();
builder.Services.AddScoped<IEspacioRepositorio, EspacioRepositorio>();
builder.Services.AddScoped<IGrupoRepositorio, GrupoRepositorio>();
builder.Services.AddScoped<IBloqueTiempoRepositorio, BloqueTiempoRepositorio>();

// ── Excel reader ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<ILectorExcel, LectorExcel>();

// ── Motores de scheduling ─────────────────────────────────────────────────────
builder.Services.AddGraphColoringEngine();
builder.Services.AddConstraintProgEngine();
builder.Services.AddGeneticEngine();

// ── Application services ──────────────────────────────────────────────────────
// CRUD Asignaturas
builder.Services.AddScoped<CreateAsignaturaService>();
builder.Services.AddScoped<GetAsignaturaByIdService>();
builder.Services.AddScoped<GetAsignaturasService>();
builder.Services.AddScoped<DeleteAsignaturaService>();
// Generación de horario
builder.Services.AddScoped<GenerarHorarioService>();

// ── OpenAPI + Controladores ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

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
app.Run();
