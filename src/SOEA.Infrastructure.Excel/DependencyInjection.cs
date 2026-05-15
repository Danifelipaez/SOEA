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
            ExcelPackage.License.SetNonCommercialPersonal("SOEA Project");

            services.AddScoped<ILectorExcel, LectorExcel>();

            return services;
        }
    }
}
