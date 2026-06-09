using System;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Entities
{
    /// <summary>
    /// Tipo de alternancia configurable (catálogo editable). Permite que la institución defina y
    /// nombre sus propias lógicas de alternancia ("Química Orgánica", "Tipo A 8 semanas", etc.) y
    /// agregue tantas como quiera, todas mapeadas sobre la base de 2 semanas (A/B) — ver
    /// <see cref="PatronBaseAlternancia"/>. La lógica de Tipos es dinámica (prueban 8/8, 8/11,
    /// "solo orgánica TipoA"); este catálogo la hace editable sin tocar el motor.
    ///
    /// El número de semanas presenciales es informativo: el motor opera sobre la abstracción A/B,
    /// no sobre el conteo real de semanas ("basta A/B por ahora").
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

        public void ActualizarDatos(string nombre, PatronBaseAlternancia patronBase, int semanasPresenciales, string? color, bool activo)
        {
            Validar(nombre, semanasPresenciales);
            Nombre = nombre.Trim();
            // Los tipos de sistema no cambian su patrón base (otros componentes dependen de él);
            // sí pueden ajustar nombre, semanas, color y estado.
            if (!EsSistema) PatronBase = patronBase;
            SemanasPresenciales = semanasPresenciales;
            if (!string.IsNullOrWhiteSpace(color)) Color = color.Trim();
            Activo = activo;
        }

        /// <summary>Mapea el patrón base al enum que entiende el motor (regla 9 / ModalidadSemanal).</summary>
        public TipoAlternancia ResolverTipoAlternancia() => PatronBase switch
        {
            PatronBaseAlternancia.PresencialEnSemanaA => TipoAlternancia.TipoA,
            PatronBaseAlternancia.PresencialEnSemanaB => TipoAlternancia.TipoB,
            _                                         => TipoAlternancia.SinAlternancia
        };

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
