var generador = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
generador.Services.AddOpenApi();

var aplicacion = generador.Build();

// Configure the HTTP request pipeline.
if (aplicacion.Environment.IsDevelopment())
{
    aplicacion.MapOpenApi();
}

aplicacion.UseHttpsRedirection();

var resumenesClima = new[]
{
    "Congelado", "Helado", "Frio", "Fresco", "Templado", "Calido", "Tibio", "Caluroso", "Asfixiante", "Abrasador"
};

aplicacion.MapGet("/pronostico-clima", () =>
{
    var pronostico = Enumerable.Range(1, 5).Select(indice =>
        new PronosticoClima
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(indice)),
            Random.Shared.Next(-20, 55),
            resumenesClima[Random.Shared.Next(resumenesClima.Length)]
        ))
        .ToArray();
    return pronostico;
})
.WithName("ObtenerPronosticoClima");

aplicacion.Run();

record PronosticoClima(DateOnly Fecha, int TemperaturaC, string? Resumen)
{
    public int TemperaturaF => 32 + (int)(TemperaturaC / 0.5556);
}
