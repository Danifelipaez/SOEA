using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Fila de la lista ordenada y activable de criterios de cesión a alternancia por saturación de
    /// espacio. El orden determina qué pool de sesiones candidatas se
    /// intenta ceder primero; un criterio inactivo no participa en la selección. Catálogo fijo de 4
    /// filas de sistema (<see cref="CriterioElegibilidadAlternancia"/>) — no es un sistema de reglas
    /// genérico, solo estos criterios existen hoy. <c>MultiplesSesiones</c> es la excepción: no otorga
    /// elegibilidad por sí sola, solo reordena entre candidatos ya elegibles por otro criterio.
    /// </summary>
    public class CriterioCesionAlternancia : EntidadBase
    {
        public CriterioElegibilidadAlternancia Criterio { get; private set; }

        /// <summary>Posición en la lista (1 = se intenta primero). Único por fila.</summary>
        public int Orden { get; private set; }

        public bool Activo { get; private set; }

        // Constructor privado para EF Core
        private CriterioCesionAlternancia() : base() { }

        public CriterioCesionAlternancia(
            Guid id,
            CriterioElegibilidadAlternancia criterio,
            int orden,
            bool activo = true) : base(id)
        {
            Validar(orden);
            Criterio = criterio;
            Orden = orden;
            Activo = activo;
        }

        public void Reordenar(int nuevoOrden)
        {
            Validar(nuevoOrden);
            Orden = nuevoOrden;
        }

        public void EstablecerActivo(bool activo)
        {
            Activo = activo;
        }

        private static void Validar(int orden)
        {
            if (orden < 1)
                throw new ArgumentException("El orden debe ser un valor positivo.");
        }

        // Ids deterministas de los 4 criterios de sistema (seed).
        // Orden inicial: MultiplesSesiones (desempate estructural) → Electiva → Optativa → Elegible.
        public static readonly Guid IdElectiva          = Guid.Parse("c0000000-0000-0000-0000-000000000001");
        public static readonly Guid IdElegible          = Guid.Parse("c0000000-0000-0000-0000-000000000002");
        public static readonly Guid IdOptativa          = Guid.Parse("c0000000-0000-0000-0000-000000000003");
        public static readonly Guid IdMultiplesSesiones = Guid.Parse("c0000000-0000-0000-0000-000000000004");
    }
}
