using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Domain.Interfaces
{
    /// <summary>
    /// Asignación EXACTA de espacios a sesiones presenciales con inicios YA FIJADOS (Fase 3,
    /// post-GA). Reemplaza el coloreo greedy anterior (M2 del análisis de espacios): con
    /// "máquinas" (espacios) no idénticas — cada sesión trae su propio conjunto de candidatos vía
    /// HC-S03/HC-S05 — el greedy por hora de inicio no es óptimo y puede descartar una asignación
    /// que sí existe. La implementación (Engine.ConstraintProg) resuelve un sub-modelo CP-SAT
    /// diminuto — los inicios ya no son variables, solo queda elegir espacio — factible en
    /// milisegundos. Engine.Genetic depende solo de esta interfaz (regla 4 CLAUDE.md: los motores
    /// solo dependen de Domain), la implementación se inyecta por DI desde el composition root.
    /// </summary>
    public interface IAsignadorEspaciosExacto
    {
        /// <returns>
        /// Mapa sesionId → espacioId para las sesiones que ocupan aula, o <c>null</c> si NO existe
        /// asignación factible (el orquestador hace fallback a Fase 2). Una sesión tiene UN aula
        /// que aplica a todas las semanas (regla 9 / ALT-05); la semana solo decide en qué
        /// NoOverlap entra ese aula — ver <see cref="Services.ModalidadSemanal.SemanasQueOcupanEspacio"/>.
        /// </returns>
        Dictionary<Guid, Guid>? Asignar(
            IReadOnlyList<Sesion> sesiones,
            int[] startPorSesion,
            int[] duracionPorSesion,
            IReadOnlyList<Espacio> espacios,
            DiaDeSemana[] diaPorIdx,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo = null,
            IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo = null);
    }
}
