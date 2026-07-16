using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

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
    /// <param name="PenalizacionPresencial">
    /// SC-PRES informativo: cuánto pesa que sesiones de alta prioridad hayan cedido presencialidad.
    /// Es CONSTANTE para el conjunto de sesiones del run (el GA no mueve la alternancia, solo el
    /// inicio) — por eso se reporta aparte en vez de sumarse al fitness, donde no aportaba señal
    /// de optimización.
    /// </param>
    public record ResultadoOptimizacion(
        IReadOnlyList<AsignacionSemanal> AsignacionesOptimizadas,
        decimal PuntajeFitness,
        int Generaciones,
        bool UsoFallback,
        decimal PenalizacionPresencial = 0m);

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
        int    PesoMaxHorasSeguidas = 3,   // SC-09: evitar > UmbralHorasSeguidas horas seguidas (blanda fuerte: domina un hueco). Antes "PesoAlmuerzo" (C2 auditoría: no pondera almuerzo, sino rachas).
        int    UmbralHorasSeguidas  = 6,   // SC-09: máximo de horas seguidas antes de penalizar. Antes hardcodeado en EvaluadorFitness (C2 auditoría).
        int    PesoBalanceSemanas   = 2,   // SC-BAL: desbalance de carga por día entre Semana A y B (Incremento 2)
        int    PesoPresencialFirst  = 4,   // SC-PRES: penaliza ceder presencialidad de sesiones de alta prioridad
        int?   Semilla              = null);

    /// <summary>
    /// Motor de Algoritmo Genético (Fase 3).
    /// Parte de la solución factible bi-semanal de la Fase 2 y la optimiza minimizando
    /// restricciones blandas centradas en la cohorte (CR-08: el docente sale del pipeline),
    /// preservando todas las restricciones duras.
    /// </summary>
    public interface IMotorGenetico
    {
        /// <param name="grupos">
        /// Grupos de estudiantes con su disponibilidad horaria.
        /// HC-G01 (hard): si un grupo declara Disponibilidad, el GA solo moverá sus sesiones
        /// a bloques dentro de esa franja — coherencia con lo que CP-SAT ya garantizó en Fase 2.
        /// </param>
        /// <param name="infoAsignatura">
        /// Por asignatura: (sesiones/semana, categoría). Alimenta SC-PRES — la penalización
        /// proporcional por ceder presencialidad de sesiones de alta prioridad. Null = sin SC-PRES.
        /// </param>
        /// <param name="ventanaPorAsignatura">
        /// HC-VH (hard): ventana horaria por asignatura. El GA solo moverá sesiones dentro de
        /// [min, max] — la misma restricción que CP-SAT ya garantizó en Fase 2. Null = sin ventanas.
        /// </param>
        /// <param name="sesionesFijasIds">
        /// Sesiones del horario base (regla 8): CP-SAT las fija por igualdad y el GA NO puede
        /// moverlas — sus genes quedan congelados en la franja de Fase 2. Null = sin fijas.
        /// </param>
        Task<ResultadoOptimizacion> OptimizarAsync(
            IEnumerable<Sesion>             sesiones,
            IEnumerable<AsignacionSemanal>  asignacionesFase2,
            IEnumerable<BloqueTiempo>       bloques,
            IEnumerable<Espacio>            espacios,
            IEnumerable<Docente>            docentes,
            IEnumerable<Grupo>?             grupos = null,
            ConfiguracionOptimizacion?      config = null,
            IReadOnlyDictionary<Guid, (int sesionesSemana, CategoriaAsignatura categoria)>? infoAsignatura = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            IReadOnlySet<Guid>?             sesionesFijasIds = null,
            CancellationToken               ct = default);
    }
}
