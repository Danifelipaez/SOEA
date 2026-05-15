using Microsoft.Extensions.DependencyInjection;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.GraphColoring
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddGraphColoringEngine(this IServiceCollection services)
        {
            // Registrar ConstructorGrafoConflictos como Singleton ya que es sin estado.
            services.AddSingleton<ConstructorGrafoConflictos>();
            
            // Registrar AgendadorColoracionGrafo
            services.AddScoped<IMotorColoracionGrafo, AgendadorColoracionGrafo>();

            return services;
        }
    }
}
