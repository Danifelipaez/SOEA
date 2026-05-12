using Microsoft.EntityFrameworkCore;
using SOEA.Domain.Interfaces;
using SOEA.Application.Features.Asignaturas;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;


//constructor
var builder = WebApplication.CreateBuilder(args);

//DbContext + Postgresql
var ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<SOEABdContext>(options =>
    options.UseNpgsql(ConnectionString));

//Inyeccion de dependencias
builder.Services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
builder.Services.AddScoped<CreateAsignaturaService>();
builder.Services.AddScoped<GetAsignaturaByIdService>();
builder.Services.AddScoped<GetAsignaturasService>();
builder.Services.AddScoped<DeleteAsignaturaService>();

//OpenAPI + Controladores
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if(app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

