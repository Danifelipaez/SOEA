namespace SOEA.Domain.Enums
{
    /// <summary>
    /// Flujo de sesión dentro de una asignatura-grupo. Cada flujo se programa por separado
    /// (su propio horario, docente y número de sesiones).
    /// </summary>
    public enum TipoFlujo
    {
        /// <summary>Componente práctico, presencial en laboratorio.</summary>
        Laboratorio,

        /// <summary>Componente teórico: salón presencial o virtual sincrónico.</summary>
        AulaVirtual
    }
}
