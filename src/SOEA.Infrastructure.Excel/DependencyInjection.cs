using Microsoft.Extensions.DependencyInjection;
using OfficeOpenXml;
using SOEA.Domain.Interfaces;

namespace SOEA.Infrastructure.Excel
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddExcelInfrastructure(this IServiceCollection services)
        {
            // Configurar EPPlus para uso no comercial
#pragma warning disable CS0618 // Type or member is obsolete
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
#pragma warning restore CS0618 // Type or member is obsolete

            services.AddScoped<ILectorExcel, LectorExcel>();

            return services;
        }
    }
}
