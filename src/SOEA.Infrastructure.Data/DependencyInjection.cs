using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SOEA.Domain.Interfaces;
using SOEA.Infrastructure.Data.Context;
using SOEA.Infrastructure.Data.Repositories;

namespace SOEA.Infrastructure.Data
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddDataInfrastructure(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<SOEABdContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddScoped<IHorarioRepositorio, HorarioRepositorio>();
            services.AddScoped<IAsignaturaRepositorio, AsignaturaRepository>();
            services.AddScoped<ISesionRepositorio, SesionRepositorio>();
            services.AddScoped<IDocenteRepositorio, DocenteRepositorio>();
            services.AddScoped<IEspacioRepositorio, EspacioRepositorio>();

            return services;
        }
    }
}
