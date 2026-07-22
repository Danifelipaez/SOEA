using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Catálogo fijo de los 3 patrones base de alternancia (TipoA, TipoB, SinAlternancia) — ver
    /// <see cref="PatronBaseAlternancia"/>. Ya no es editable desde la UI (el algoritmo decide
    /// dinámicamente qué asignaturas ceden a alternancia vía <c>CriterioCesionAlternancia</c>);
    /// esta entidad solo sobrevive como catálogo de IDs de sistema estables, referenciados por
    /// <see cref="Sesion.PatronAlternanciaId"/> para trazabilidad de qué patrón se aplicó.
    /// </summary>
    public class TipoAlternanciaConfig : EntidadBase
    {
        public string Nombre { get; private set; } = "";
        public PatronBaseAlternancia PatronBase { get; private set; }

        /// <summary>Semanas presenciales en el semestre (informativo, no afecta al motor).</summary>
        public int SemanasPresenciales { get; private set; }

        /// <summary>Color hex para distinguirlo en la UI (ej. "#1565c0").</summary>
        public string Color { get; private set; } = "#607d8b";

        /// <summary>Tipos de sistema (TipoA, TipoB, SinAlternancia): no se pueden eliminar.</summary>
        public bool EsSistema { get; private set; }

        public bool Activo { get; private set; } = true;

        // Constructor privado para EF Core
        private TipoAlternanciaConfig() : base() { }

        public TipoAlternanciaConfig(
            Guid id,
            string nombre,
            PatronBaseAlternancia patronBase,
            int semanasPresenciales = 0,
            string? color = null,
            bool esSistema = false) : base(id)
        {
            Validar(nombre, semanasPresenciales);
            Nombre = nombre.Trim();
            PatronBase = patronBase;
            SemanasPresenciales = semanasPresenciales;
            Color = string.IsNullOrWhiteSpace(color) ? "#607d8b" : color.Trim();
            EsSistema = esSistema;
            Activo = true;
        }

        private static void Validar(string nombre, int semanasPresenciales)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new ArgumentException("El nombre del tipo de alternancia no puede estar vacío.");
            if (semanasPresenciales < 0 || semanasPresenciales > 52)
                throw new ArgumentException("Las semanas presenciales deben estar entre 0 y 52.");
        }

        // Ids deterministas de los 3 tipos de sistema (seed). Estables para referenciarlos.
        public static readonly Guid IdTipoA          = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        public static readonly Guid IdTipoB          = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        public static readonly Guid IdSinAlternancia = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    }
}
