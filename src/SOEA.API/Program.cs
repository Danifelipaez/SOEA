using Microsoft.EntityFrameworkCore;
using SOEA.Application.Features.Asignaturas;
using SOEA.Application.Features.Horario;
using SOEA.Domain.Interfaces;
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
using SOEA.Engine.GraphColoring;
using SOEA.Infrastructure.Data;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;

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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAngularDev");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
