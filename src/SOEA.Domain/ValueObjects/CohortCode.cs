using System;

namespace SOEA.Domain.ValueObjects
{
    /// <summary>
    /// Objeto de valor que representa el código de una cohorte o grupo.
    /// Encapsula las reglas de formato y validación para asegurar que siempre tenga un estado válido.
    /// </summary>
    public record CohortCode
    {
        public string Value { get; init; }

        public CohortCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("El código de la cohorte no puede estar vacío o compuesto solo por espacios.", nameof(value));

            // Aquí se pueden agregar validaciones de formato específico, por ejemplo si debe cumplir con una expresión regular
            Value = value.Trim().ToUpperInvariant();
        }

        public override string ToString() => Value;
    }
}
