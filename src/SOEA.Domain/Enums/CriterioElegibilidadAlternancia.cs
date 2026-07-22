namespace SOEA.Domain.Enums
{
    /// <summary>
    /// Criterio con el que una sesión/asignatura se vuelve candidata a ceder a alternancia por
    /// saturación de espacio. Ver <see cref="Entities.CriterioCesionAlternancia"/>.
    /// </summary>
    public enum CriterioElegibilidadAlternancia
    {
        /// <summary>Asignaturas con <see cref="CategoriaAsignatura.Electiva"/>.</summary>
        Electiva,

        /// <summary>Asignaturas marcadas explícitamente vía <c>Asignatura.EsCandidataAlternancia</c>.</summary>
        Elegible,

        /// <summary>Asignaturas con <see cref="CategoriaAsignatura.Optativa"/>.</summary>
        Optativa,

        /// <summary>
        /// Asignaturas con 2 o más sesiones semanales (cualquier categoría). A diferencia de los demás
        /// criterios, NO otorga elegibilidad por sí solo — solo desempata el orden de cesión entre
        /// sesiones que ya matchean otro criterio activo (Electiva/Optativa/Elegible). Evita que
        /// cualquier asignatura con 2+ sesiones se vuelva candidata solo por ese hecho.
        /// </summary>
        MultiplesSesiones
    }
}
