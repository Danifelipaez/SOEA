using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Reglas puras de sesión reutilizadas por CP-SAT, el validador post-generación y la creación
    /// manual (A5) — un solo lugar decide si dos días cumplen la separación mínima.
    /// </summary>
    public static class ReglasSesion
    {
        /// <summary>
        /// HC-SEP: dos sesiones semanales de la misma (grupo, asignatura, tipo de sesión) deben
        /// caer en días separados por al menos 2 posiciones en la semana (p. ej. lunes/miércoles
        /// cumple, lunes/martes no). El orden de <see cref="DiaDeSemana"/> (Lunes=0..Sábado=5) ya
        /// sigue el orden real de la semana.
        /// </summary>
        public static bool SeparacionDiasOk(DiaDeSemana diaA, DiaDeSemana diaB) =>
            Math.Abs((int)diaA - (int)diaB) >= 2;
    }
}
