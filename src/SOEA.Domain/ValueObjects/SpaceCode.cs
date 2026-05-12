using System;

namespace SOEA.Domain.ValueObjects
{
    /// <summary>
    /// Objeto de valor que representa el código de un espacio físico (aula, laboratorio, etc.).
    /// Encapsula la validación y el formato.
    /// </summary>
    public record SpaceCode
    {
        public string Value { get; init; }

        public SpaceCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("El código de espacio no puede estar vacío o compuesto solo por espacios.", nameof(value));

            Value = value.Trim().ToUpperInvariant();
        }

        public override string ToString() => Value;
    }
}
