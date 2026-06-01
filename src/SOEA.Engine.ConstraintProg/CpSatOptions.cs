namespace SOEA.Engine.ConstraintProg
{
    /// <summary>
    /// Configuración del motor CP-SAT. Inyectada desde <c>Program.cs</c> a partir de la
    /// sección <c>CpSat</c> de configuración. Mantiene el motor stateless y desacoplado de
    /// ASP.NET (CLAUDE.md regla 4): es un POCO de dominio del motor, no una dependencia externa.
    /// </summary>
    public class CpSatOptions
    {
        /// <summary>
        /// Si es true, exporta el modelo a <c>cp_model_debug.txt</c> en cada solve.
        /// Default false: escribir en disco en cada request llena el disco y filtra los IDs
        /// de docentes/sesiones del modelo (P0.2 auditoría). Solo habilitar en depuración local.
        /// </summary>
        public bool ExportarModelo { get; set; } = false;

        /// <summary>Tiempo máximo de búsqueda del solver en segundos.</summary>
        public int TimeoutSegundos { get; set; } = 120;
    }
}
