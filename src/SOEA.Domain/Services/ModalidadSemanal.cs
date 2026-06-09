using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente única de verdad para la regla 9 (alternancia): deriva la modalidad de una sesión
    /// en una semana concreta del ciclo A/B. Dato FIJO — no lo decide ningún motor.
    /// Usada por CP-SAT (Fase 2) y por el GA (Fase 3) para evitar divergencias.
    ///   - Sesión totalmente virtual ⇒ virtual en ambas semanas.
    ///   - TipoA ⇒ presencial en A, virtual en B.
    ///   - TipoB ⇒ presencial en B, virtual en A.
    ///   - SinAlternancia ⇒ presencial en ambas.
    /// </summary>
    public static class ModalidadSemanal
    {
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
    }
}
