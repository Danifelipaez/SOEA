using Microsoft.Extensions.DependencyInjection;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.Genetic
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddGeneticEngine(this IServiceCollection services)
        {
            services.AddSingleton<IMotorGenetico, MotorGenetico>();
            return services;
        }
    }
}
