namespace SOEA.Domain.Enums
{
    /// <summary>
    /// Patrón base de un tipo de alternancia sobre el modelo de 2 semanas representativas (A/B).
    /// Es la pieza que el motor entiende; los tipos configurables del catálogo
    /// (<see cref="Entities.TipoAlternanciaConfig"/>) se mapean a uno de estos tres patrones.
    ///   - PresencialEnSemanaA ⇒ presencial en semanas A (pares), virtual en B.
    ///   - PresencialEnSemanaB ⇒ presencial en semanas B (impares), virtual en A.
    ///   - SinAlternancia      ⇒ presencial en ambas.
    /// </summary>
    public enum PatronBaseAlternancia
    {
        PresencialEnSemanaA,
        PresencialEnSemanaB,
        SinAlternancia
    }
}
