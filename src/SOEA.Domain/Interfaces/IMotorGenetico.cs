using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Resultado de la Fase 3 (Algoritmo Genético).
    /// El GA optimiza restricciones blandas sobre el modelo bi-semanal: devuelve las
    /// <see cref="AsignacionSemanal"/> optimizadas (dos por sesión, Semana A/B), NO sesiones sueltas.
    /// </summary>
    /// <param name="AsignacionesOptimizadas">Asignaciones finales (optimizadas o, si hubo fallback, las de Fase 2).</param>
    /// <param name="PuntajeFitness">Fitness del mejor cromosoma (menor = mejor).</param>
    /// <param name="Generaciones">Generaciones ejecutadas.</param>
    /// <param name="UsoFallback">True si el GA devolvió la solución de Fase 2 (no pudo mejorar de forma factible).</param>
    public record ResultadoOptimizacion(
        IReadOnlyList<AsignacionSemanal> AsignacionesOptimizadas,
        decimal PuntajeFitness,
        int Generaciones,
        bool UsoFallback);

    /// <summary>
    /// Parámetros de ejecución del algoritmo genético y pesos de restricciones blandas.
    /// Todos los campos tienen valores por defecto; el frontend puede sobreescribirlos.
    /// </summary>
    /// <param name="Semilla">Semilla del RNG. Null = aleatoria (producción); fija = reproducible (tests).</param>
    public record ConfiguracionOptimizacion(
        int    TamañoPoblacion      = 50,
        int    MaxGeneraciones      = 200,
        double ProbabilidadMutacion = 0.05,
        double ProbabilidadCruce    = 0.80,
        int    UmbralConvergencia   = 30,
        int    PesoErgo             = 3,   // SC-01: minimizar huecos ociosos entre sesiones
        int    PesoTiempos          = 2,   // SC-06: balancear carga entre días disponibles
        int    PesoAlmuerzo         = 3,   // SC-09: evitar > 6 horas seguidas (blanda fuerte: domina un hueco)
        int    PesoBalanceSemanas   = 2,   // SC-BAL: desbalance de carga por día entre Semana A y B (Incremento 2)
        int?   Semilla              = null);

    /// <summary>
    /// Motor de Algoritmo Genético (Fase 3).
    /// Parte de la solución factible bi-semanal de la Fase 2 y la optimiza minimizando
    /// restricciones blandas centradas en el docente, preservando todas las restricciones duras.
    /// </summary>
    public interface IMotorGenetico
    {
        Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion>             sesiones,
            IEnumerable<AsignacionSemanal>  asignacionesFase2,
            IEnumerable<BloqueTiempo>       bloques,
            IEnumerable<Espacio>            espacios,
            IEnumerable<Docente>            docentes,
            ConfiguracionOptimizacion?      config = null);
    }
}
