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

        /// <summary>
        /// Número de workers de búsqueda en paralelo del solver. Default 0: OR-Tools
        /// auto-detecta los cores disponibles. Útil fijarlo explícitamente al escalar
        /// el App Service (más cores) para asegurar diversidad de búsqueda.
        /// </summary>
        public int NumWorkers { get; set; } = 0;

        /// <summary>
        /// Si es true, ante una infactibilidad SIN causa explicada por ningún pre-check (el
        /// catch-all final tras un solve CP-SAT genuinamente INFEASIBLE), reintenta el solve una
        /// vez por grupo excluyéndolo, para reportar cuáles grupos son responsables. Default false:
        /// cada intento es un solve CP-SAT completo adicional — herramienta de depuración, no para
        /// el camino caliente de producción.
        /// </summary>
        public bool SweepGrupos { get; set; } = false;

        /// <summary>Tope de grupos candidatos para el barrido de <see cref="SweepGrupos"/> — evita
        /// un costo O(N) descontrolado en runs con muchos grupos.</summary>
        public int SweepGruposMaximo { get; set; } = 20;

        /// <summary>
        /// Ante una infactibilidad que ningún pre-check explicó, reintenta el solve UNA vez sin las
        /// restricciones de aula. Si esa relajación es factible, la causa son los espacios y el
        /// motivo se reporta como <c>Espacio</c> en vez de <c>Otro</c>.
        ///
        /// Es lo que permite que la Semana B se active de forma reactiva: el pre-check agregado de
        /// demanda vs capacidad solo ve el total semanal, no los cuellos de botella por bloque (p.
        /// ej. todos los grupos con disponibilidad que choca el martes a las 08:00). Sin esta
        /// clasificación esos casos devolverían <c>Otro</c>, el bucle de cesión de
        /// GenerarHorarioService no cedería nunca, y un horario que sí tiene solución con alternancia
        /// fallaría. Default true: un único solve extra, y solo en el camino de fallo.
        /// </summary>
        public bool ClasificarInfactibilidadEspacio { get; set; } = true;
    }
}
