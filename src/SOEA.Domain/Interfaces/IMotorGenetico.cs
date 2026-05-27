using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Resultado de la Fase 3 (Algoritmo Genético).
    /// </summary>
    public record ResultadoOptimizacion(
        IReadOnlyList<Sesion> SesionesOptimizadas,
        decimal PuntajeFitness,
        int Generaciones);

    /// <summary>
    /// Parámetros de ejecución del algoritmo genético y pesos de restricciones blandas.
    /// Todos los campos tienen valores por defecto; el frontend puede sobreescribirlos.
    /// </summary>
    public record ConfiguracionOptimizacion(
        int    TamañoPoblacion      = 50,
        int    MaxGeneraciones      = 200,
        double ProbabilidadMutacion = 0.05,
        double ProbabilidadCruce    = 0.80,
        int    UmbralConvergencia   = 30,
        int    PesoErgo             = 3,   // SC-01: horario compacto del docente
        int    PesoTiempos          = 2,   // SC-06: carga distribuida / tiempos muertos
        int    PesoAlmuerzo         = 1);  // SC-09: evitar concentración de horas en un día

    /// <summary>
    /// Motor de Algoritmo Genético (Fase 3).
    /// Toma la solución factible de la Fase 2 y la optimiza minimizando restricciones blandas.
    /// </summary>
    public interface IMotorGenetico
    {
        Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion>      sesionesFactibles,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio>     espacios,
            IEnumerable<Docente>     docentes,
            ConfiguracionOptimizacion? config = null);
    }
}
