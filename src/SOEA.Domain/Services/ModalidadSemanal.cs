using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente única de verdad del modelo semanal (regla 9 / ALT-05).
    ///
    /// INVARIANTE: una sesión existe en TODAS las semanas. Solo su MODALIDAD puede variar, y solo
    /// si alterna. La franja y el aula son un dato único que aplica a todo el semestre — nunca
    /// difieren entre semana A y semana B. (Antes existía "ALT-06", que permitía a una sesión
    /// SinAlternancia caer en franjas distintas en A y B: era una reescritura equivocada de ALT-05
    /// y producía dos horarios incompatibles presentados como uno.)
    ///
    /// Consecuencias que consumen CP-SAT, el GA y el validador:
    ///   - Todo eje de TIEMPO (HC-C01 cohorte, HC-SEP, HC-VH, HC-G01) es independiente de la semana.
    ///   - Solo el eje de AULA es sensible a la semana, porque una sesión virtual libera su aula:
    ///     ver <see cref="SemanasQueOcupanEspacio"/>.
    ///   - Se persiste UNA fila <see cref="AsignacionSemanal"/> por sesión, en su
    ///     <see cref="SemanaCanonica"/>. La contraparte virtual se deriva al mapear el DTO.
    /// </summary>
    public static class ModalidadSemanal
    {
        private static readonly SemanaAcademica[] Ambas   = { SemanaAcademica.A, SemanaAcademica.B };
        private static readonly SemanaAcademica[] SoloA   = { SemanaAcademica.A };
        private static readonly SemanaAcademica[] SoloB   = { SemanaAcademica.B };
        private static readonly SemanaAcademica[] Ninguna = { };

        /// <summary>
        /// Modalidad de una sesión en una semana concreta del ciclo A/B. Dato FIJO — no lo decide
        /// ningún motor.
        ///   - Sesión totalmente virtual ⇒ virtual en ambas semanas.
        ///   - TipoA ⇒ presencial en A, virtual en B.
        ///   - TipoB ⇒ presencial en B, virtual en A.
        ///   - SinAlternancia ⇒ presencial en ambas.
        /// </summary>
        public static Modalidad Derivar(Sesion sesion, SemanaAcademica semana)
        {
            if (sesion.Modalidad == Modalidad.Virtual)
                return Modalidad.Virtual;

            return sesion.Alternancia switch
            {
                TipoAlternancia.TipoA => semana == SemanaAcademica.A ? Modalidad.Presencial : Modalidad.Virtual,
                TipoAlternancia.TipoB => semana == SemanaAcademica.B ? Modalidad.Presencial : Modalidad.Virtual,
                _                     => Modalidad.Presencial // SinAlternancia: presencial en ambas
            };
        }

        /// <summary>
        /// Semana de la ÚNICA fila <see cref="AsignacionSemanal"/> que se persiste para esta sesión:
        /// la semana en la que es presencial. TipoB ⇒ B; todo lo demás ⇒ A. Para una sesión que no
        /// alterna, "A" significa "todas las semanas", no "solo la semana impar".
        /// </summary>
        public static SemanaAcademica SemanaCanonica(Sesion sesion) =>
            sesion.Alternancia == TipoAlternancia.TipoB && sesion.Modalidad != Modalidad.Virtual
                ? SemanaAcademica.B
                : SemanaAcademica.A;

        /// <summary>Modalidad de la fila persistida (ver <see cref="SemanaCanonica"/>).</summary>
        public static Modalidad ModalidadCanonica(Sesion sesion) =>
            Derivar(sesion, SemanaCanonica(sesion));

        /// <summary>
        /// Semanas en las que la sesión OCUPA FÍSICAMENTE un aula. Es la única pieza del modelo
        /// que depende de la semana, y la razón por la que emparejar libera capacidad:
        ///   - Virtual pura ⇒ ninguna (no reserva aula nunca).
        ///   - SinAlternancia ⇒ ambas (por eso NO libera capacidad en ninguna semana).
        ///   - TipoA ⇒ solo A · TipoB ⇒ solo B (liberan la semana contraria para su pareja).
        /// Depende de <see cref="Sesion.Alternancia"/>, NO de <see cref="Sesion.ParejaAlternanciaId"/>:
        /// una TipoA suelta (sin pareja) libera igualmente su aula en B, solo que nadie la aprovecha.
        /// </summary>
        public static IReadOnlyList<SemanaAcademica> SemanasQueOcupanEspacio(Sesion sesion) =>
            SemanasQueOcupanEspacio(sesion.Alternancia, sesion.Modalidad);

        /// <summary>
        /// Igual que la sobrecarga sobre <see cref="Sesion"/>, para cuando la entidad todavía no
        /// existe (validación previa a crear una sesión manual).
        /// </summary>
        public static IReadOnlyList<SemanaAcademica> SemanasQueOcupanEspacio(
            TipoAlternancia alternancia, Modalidad modalidad)
        {
            if (modalidad == Modalidad.Virtual) return Ninguna;
            return alternancia switch
            {
                TipoAlternancia.TipoA => SoloA,
                TipoAlternancia.TipoB => SoloB,
                _                     => Ambas
            };
        }

        /// <summary>
        /// True si una sesión aún no creada, con <paramref name="alternancia"/> y
        /// <paramref name="modalidad"/>, compartiría semana de aula con <paramref name="existente"/>.
        /// </summary>
        public static bool CompartenSemanaDeEspacio(
            TipoAlternancia alternancia, Modalidad modalidad, Sesion existente)
        {
            var semanasNueva = SemanasQueOcupanEspacio(alternancia, modalidad);
            var semanasOtra  = SemanasQueOcupanEspacio(existente);
            foreach (var w in semanasNueva)
                foreach (var o in semanasOtra)
                    if (w == o) return true;
            return false;
        }

        /// <summary>
        /// True si las dos sesiones pueden coincidir físicamente en algún aula: comparten al menos
        /// una semana de ocupación. Dos miembros de una pareja (TipoA/TipoB) devuelven false — por
        /// eso pueden compartir bloque y aula sin ser un conflicto.
        /// </summary>
        public static bool CompartenSemanaDeEspacio(Sesion a, Sesion b)
        {
            var semanasA = SemanasQueOcupanEspacio(a);
            var semanasB = SemanasQueOcupanEspacio(b);
            foreach (var semana in semanasA)
                foreach (var otra in semanasB)
                    if (semana == otra) return true;
            return false;
        }
    }
}
