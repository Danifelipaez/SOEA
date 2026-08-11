using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.ValueObjects;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente ÚNICA del dominio de inicios válidos de una sesión sobre la grilla canónica:
    ///   cabe-en-día (BloquesPlanner) ∩ HC-G01 (disponibilidad del grupo, por día) ∩ HC-VH (ventana
    ///   de la asignatura).
    /// Antes este cálculo vivía cuadruplicado (Fase 1 sin filtros, CP-SAT con G01+VH, GA solo G01,
    /// validador sin dominio), lo que permitía que un motor "no conociera" una restricción que otro
    /// imponía. Todo motor y el validador post-generación deben derivar franja/ventana de aquí.
    /// </summary>
    public static class CalculadorDominioSesion
    {
        /// <summary>
        /// HC-VH: el intervalo [horaInicio, horaInicio + duracion] cae dentro de [min, max].
        /// <paramref name="duracion"/> ya viene redondeada hacia arriba (ceil): sobre-reserva
        /// conservadora, nunca sub-reserva.
        /// </summary>
        public static bool CumpleVentana(TimeOnly horaInicio, int duracion, TimeOnly? min, TimeOnly? max)
        {
            if (min.HasValue && horaInicio < min.Value) return false;
            if (max.HasValue && horaInicio.AddHours(duracion) > max.Value) return false;
            return true;
        }

        /// <summary>
        /// HC-G01: índices de bloque cuyo [día, horaInicio, horaFin] cae en la disponibilidad
        /// declarada del grupo (A1 — granularidad por día, no la franja Matutino/Vespertino de
        /// antes). Null = sin restricción (disponibilidad vacía o sin coincidencias — semántica
        /// histórica: un filtro que vaciaría el dominio se trata como "sin información").
        /// </summary>
        public static HashSet<int>? BloquesPermitidos(
            IReadOnlyList<BloqueTiempo> bloques,
            DisponibilidadSemanal disponibilidad)
        {
            var permitidos = new HashSet<int>();
            for (int i = 0; i < bloques.Count; i++)
                if (disponibilidad.PermiteBloque(bloques[i].Dia, bloques[i].HoraInicio, bloques[i].HoraFin))
                    permitidos.Add(i);
            return permitidos.Count > 0 && permitidos.Count < bloques.Count ? permitidos : null;
        }

        /// <summary>
        /// Dominio completo de inicios para una sesión de <paramref name="duracion"/> horas:
        /// cabe-en-día ∩ HC-G01 (si <paramref name="bloquesPermitidosGrupo"/> no es null)
        /// ∩ HC-VH (si hay ventana). El resultado puede ser vacío: el llamador decide si eso es
        /// infactibilidad (CP-SAT) o congelar el gen (GA).
        /// </summary>
        public static int[] StartsPermitidos(
            int duracion,
            IReadOnlyList<BloqueTiempo> bloques,
            IDictionary<DiaDeSemana, (int firstIdx, int lastIdx)> rangos,
            DiaDeSemana[] diaPorIdx,
            HashSet<int>? bloquesPermitidosGrupo = null,
            TimeOnly? ventanaMin = null,
            TimeOnly? ventanaMax = null)
        {
            IEnumerable<int> starts = BloquesPlanner.StartsValidos(
                duracion, bloques.Count, rangos, diaPorIdx, bloquesDisponibles: null);

            if (bloquesPermitidosGrupo is not null)
                starts = starts.Where(bloquesPermitidosGrupo.Contains);

            if (ventanaMin.HasValue || ventanaMax.HasValue)
                starts = starts.Where(s => CumpleVentana(bloques[s].HoraInicio, duracion, ventanaMin, ventanaMax));

            return starts.ToArray();
        }
    }
}
