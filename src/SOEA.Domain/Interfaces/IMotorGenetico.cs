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
    /// Motor de Algoritmo Genético (Fase 3).
    /// Toma la solución factible de la Fase 2 y la optimiza minimizando restricciones blandas.
    /// </summary>
    public interface IMotorGenetico
    {
        Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion> sesionesFactibles,
            IEnumerable<BloqueTiempo> bloques,
            IEnumerable<Espacio> espacios,
            IEnumerable<Docente> docentes);
    }
}
