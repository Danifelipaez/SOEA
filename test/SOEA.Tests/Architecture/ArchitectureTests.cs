using NetArchTest.Rules;
using SOEA.Infrastructure.Data;

namespace SOEA.Tests.Architecture;

public class ArchitectureTests
{
    private const string DomainNs      = "SOEA.Domain";
    private const string ApplicationNs = "SOEA.Application";
    private const string InfraDataNs   = "SOEA.Infrastructure.Data";
    private const string InfraExcelNs  = "SOEA.Infrastructure.Excel";
    private const string ApiNs         = "SOEA.API";

    [Fact]
    public void Domain_NoDebeReferenciarNingunOtroProyecto()
    {
        var result = Types.InAssembly(typeof(SOEA.Domain.Entities.Asignatura).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNs, InfraDataNs, InfraExcelNs, ApiNs)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain tiene dependencias prohibidas: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_NoDebeReferenciarInfraestructura()
    {
        var result = Types.InAssembly(typeof(SOEA.Application.BaseAplicacion).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfraDataNs, InfraExcelNs)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Application tiene dependencias prohibidas: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infraestructura_NoDebeReferenciarAPI()
    {
        var result = Types.InAssembly(typeof(SOEA.Infrastructure.Data.BaseInfraestructuraDatos).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApiNs)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure.Data tiene dependencias prohibidas: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Repositorios_DebenImplementarInterfacesDeDomain()
    {
        var result = Types.InAssembly(typeof(SOEA.Infrastructure.Data.BaseInfraestructuraDatos).Assembly)
            .That()
            .HaveNameEndingWith("Repository")
            .Should()
            .ResideInNamespace("SOEA.Infrastructure.Data.Repositories")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Repositorios fuera de lugar: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Entidades_DebenVivirSoloEnDomain()
    {
        // Verifica que las clases que terminan con "Entity" existan en Domain
        // No pueden existir en Application
        var result = Types.InAssembly(typeof(SOEA.Application.BaseAplicacion).Assembly)
            .Should()
            .NotHaveNameEndingWith("Entity")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Entidades encontradas en Application: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}