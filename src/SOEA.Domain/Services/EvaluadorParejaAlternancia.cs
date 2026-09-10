using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Domain.Services
{
    /// <summary>Por qué dos sesiones no pueden formar una pareja de alternancia.</summary>
    public enum MotivoRechazoPareja
    {
        /// <summary>Sí pueden emparejarse.</summary>
        Ninguno,
        MismaSesion,
        MismaAsignatura,
        /// <summary>
        /// Mismo grupo. HC-C01 (solape de cohorte) es independiente de la semana y HC-ALT fuerza
        /// el mismo bloque, así que una pareja del mismo grupo es estructuralmente infactible.
        /// </summary>
        MismoGrupo,
        DuracionDistinta,
        RequisitoIncompatible,
        /// <summary>No comparten ninguna franja válida (ventana de asignatura ∩ disponibilidad de grupo).</summary>
        SinFranjaComun,
        /// <summary>No hay ningún aula que sirva a las dos (tipo, espacio fijo o aforo).</summary>
        SinAulaComun
    }

    /// <summary>
    /// Fuente ÚNICA de la regla de emparejamiento de alternancia. Espejo de
    /// <see cref="CalculadorDominioSesion"/> (eje temporal) y <see cref="CalculadorEspaciosSesion"/>
    /// (eje espacial): aquí se decide si dos sesiones pueden compartir bloque y aula alternando
    /// semanas — una presencial en A y virtual en B, la otra al revés.
    ///
    /// Emparejar es el ÚNICO mecanismo que libera capacidad de aulas: una sesión que no alterna
    /// ocupa su aula en las dos semanas (ver <see cref="ModalidadSemanal.SemanasQueOcupanEspacio"/>).
    ///
    /// Las compuertas de franja y aula son lo que evita que el emparejamiento vuelva el modelo
    /// infactible: los dominios que recibe se calculan con las MISMAS entradas que usará CP-SAT
    /// (<see cref="CalculadorDominioSesion.StartsPermitidos"/> y
    /// <see cref="CalculadorEspaciosSesion.Candidatos"/>), así que una pareja aceptada aquí siempre
    /// tiene al menos un (bloque, aula) donde CP-SAT puede colocarla.
    /// </summary>
    public static class EvaluadorParejaAlternancia
    {
        /// <param name="dominioA">Inicios válidos de <paramref name="a"/> (<see cref="CalculadorDominioSesion.StartsPermitidos"/>).</param>
        /// <param name="dominioB">Inicios válidos de <paramref name="b"/>.</param>
        /// <param name="aulasA">Índices de aula candidatos de <paramref name="a"/>, YA filtrados por aforo del grupo.</param>
        /// <param name="aulasB">Índices de aula candidatos de <paramref name="b"/>, YA filtrados por aforo.</param>
        /// <remarks>
        /// El aforo se filtra fuera (el llamador ya conoce los estudiantes por grupo y construye
        /// estas listas para CP-SAT de todos modos), así que un rechazo por aforo llega aquí como
        /// <see cref="MotivoRechazoPareja.SinAulaComun"/>.
        /// </remarks>
        public static MotivoRechazoPareja Evaluar(
            Sesion a,
            Sesion b,
            IReadOnlyDictionary<Guid, Grupo> grupoPorId,
            IReadOnlyList<int> dominioA,
            IReadOnlyList<int> dominioB,
            IReadOnlyList<int> aulasA,
            IReadOnlyList<int> aulasB)
        {
            if (a.Id == b.Id)                     return MotivoRechazoPareja.MismaSesion;
            if (a.AsignaturaId == b.AsignaturaId) return MotivoRechazoPareja.MismaAsignatura;
            if (a.GrupoId == b.GrupoId)           return MotivoRechazoPareja.MismoGrupo;
            if (a.DuracionHoras != b.DuracionHoras) return MotivoRechazoPareja.DuracionDistinta;

            if (!RequisitosCompatibles(a, b, grupoPorId)) return MotivoRechazoPareja.RequisitoIncompatible;

            // HC-ALT exige el MISMO bloque y el MISMO aula para los dos miembros. Sin intersección
            // en cualquiera de los dos ejes, la pareja hace infactible el modelo completo — y con
            // motivo distinto de Espacio, que es justo el fallo que rompe el bucle de cesión.
            if (FranjasComunes(dominioA, dominioB).Length == 0) return MotivoRechazoPareja.SinFranjaComun;
            if (AulasComunes(aulasA, aulasB).Length == 0)       return MotivoRechazoPareja.SinAulaComun;

            return MotivoRechazoPareja.Ninguno;
        }

        /// <summary>Inicios donde AMBAS sesiones pueden empezar (orden ascendente).</summary>
        public static int[] FranjasComunes(IReadOnlyList<int> a, IReadOnlyList<int> b) => Interseccion(a, b);

        /// <summary>Aulas que sirven a AMBAS sesiones (orden ascendente).</summary>
        public static int[] AulasComunes(IReadOnlyList<int> a, IReadOnlyList<int> b) => Interseccion(a, b);

        private static int[] Interseccion(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count == 0 || b.Count == 0) return Array.Empty<int>();
            var enB = new HashSet<int>(b);
            return a.Where(enB.Contains).Distinct().OrderBy(v => v).ToArray();
        }

        /// <summary>
        /// Requisito de espacio del grupo para el tipo de sesión de cada una. Ambas sin requisito
        /// ⇒ compatibles (comparten el default por TipoSesion, que la intersección de aulas
        /// verifica de todos modos). Una con requisito y la otra sin él ⇒ no garantizado.
        /// </summary>
        private static bool RequisitosCompatibles(Sesion a, Sesion b, IReadOnlyDictionary<Guid, Grupo> grupoPorId)
        {
            RequisitoEspacio? RequisitoDe(Sesion s) =>
                s.GrupoId.HasValue && grupoPorId.TryGetValue(s.GrupoId.Value, out var g)
                    ? g.RequisitosEspacio.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(s))
                    : null;

            var ra = RequisitoDe(a);
            var rb = RequisitoDe(b);
            if (ra is null && rb is null) return true;
            if (ra is null || rb is null) return false;
            return ra.EspacioId == rb.EspacioId && ra.TipoEspacio == rb.TipoEspacio;
        }
    }
}
