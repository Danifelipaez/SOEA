using System;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Clase base para todas las entidades del dominio SOEA.
    /// Centraliza el identificador único y el patrón de igualdad por identidad.
    /// </summary>
    public abstract class BaseEntity
    {
        public Guid Id { get; protected set; }

        protected BaseEntity() { }

        protected BaseEntity(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("El ID de la entidad no puede ser vacío.", nameof(id));
            Id = id;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not BaseEntity other) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        public override int GetHashCode() => Id.GetHashCode();
    }
}
