using Microsoft.Extensions.DependencyInjection;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.ConstraintProg
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddConstraintProgEngine(this IServiceCollection services)
        {
            services.AddSingleton<IMotorConstraintProgramming, MotorConstraintProgramming>();
            return services;
        }
    }
}
