using Microsoft.EntityFrameworkCore;
using SOEA.Application.Interfaces;
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
builder.Services.AddScoped<IAsignaturaRepository, AsignaturaRepository>();
builder.Services.AddScoped<CreateAsignaturaService>();

//Swagger + Controladores
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if(app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

