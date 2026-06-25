namespace SOEA.Domain.Enums
{
    /// <summary>
    /// Categoría curricular que rige la prioridad de preservación de presencialidad
    /// (presencial-first): las obligatorias conservan presencialidad primero; las electivas
    /// son las primeras candidatas a recibir alternancia / virtualización.
    /// </summary>
    public enum CategoriaAsignatura
    {
        Obligatoria,
        Optativa,
        Electiva
    }
}
