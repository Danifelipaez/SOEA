using System;
using Microsoft.Extensions.DependencyInjection;
using SOEA.Domain.Interfaces;

namespace SOEA.Engine.ConstraintProg
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddConstraintProgEngine(
            this IServiceCollection services,
            Action<CpSatOptions>? configure = null)
        {
            var options = new CpSatOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<IMotorConstraintProgramming, MotorConstraintProgramming>();
            return services;
        }
    }
}
