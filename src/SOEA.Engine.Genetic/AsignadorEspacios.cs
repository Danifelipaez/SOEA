using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Asigna aulas a las sesiones presenciales DESPUÉS de que el GA fijó los inicios.
    /// Como los inicios son fijos, el problema se reduce a un coloreo de intervalos por
    /// (espacio, semana): greedy por inicio = óptimo para intervalos. Garantiza HC-S01
    /// (no dos sesiones presenciales solapadas en el mismo aula/semana), HC-S03 (si la
    /// sesión requiere laboratorio, el aula es laboratorio), HC-S05 (espacio fijo de la
    /// asignatura: si la sesión trae EspacioId y ese espacio existe, es el único candidato)
    /// y HC-CAP (aforo: Capacidad ≥ estudiantes inscritos del grupo) — los mismos filtros
    /// de candidatos que CP-SAT aplica en Fase 2.
    ///
    /// Devuelve un mapa (sesionId, semana) → espacioId para las semanas presenciales, o
    /// <c>null</c> si NO existe asignación factible (el orquestador hará fallback a Fase 2).
    /// </summary>
    internal static class AsignadorEspacios
    {
        public static Dictionary<(Guid sesionId, SemanaAcademica semana), Guid>? Asignar(
            IReadOnlyList<Sesion> sesiones,
            int[] startAPorSesion,
            int[] startBPorSesion,
            int[] duracionPorSesion,
            IReadOnlyList<Espacio> espacios,
            DiaDeSemana[] diaPorIdx,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo = null)
        {
            var resultado = new Dictionary<(Guid, SemanaAcademica), Guid>();
            if (espacios.Count == 0)
            {
                // Sin espacios: solo es factible si NINGUNA sesión es presencial en ninguna semana.
                bool hayPresencial = sesiones.Any(s =>
                    ModalidadSemanal.Derivar(s, SemanaAcademica.A) == Modalidad.Presencial ||
                    ModalidadSemanal.Derivar(s, SemanaAcademica.B) == Modalidad.Presencial);
                return hayPresencial ? null : resultado;
            }

            foreach (var semana in new[] { SemanaAcademica.A, SemanaAcademica.B })
            {
                var startPorSesion = semana == SemanaAcademica.A ? startAPorSesion : startBPorSesion;

                // Sesiones presenciales en esta semana, ordenadas por inicio (greedy óptimo).
                var presenciales = Enumerable.Range(0, sesiones.Count)
                    .Where(i => ModalidadSemanal.Derivar(sesiones[i], semana) == Modalidad.Presencial)
                    .OrderBy(i => startPorSesion[i])
                    .ToList();

                // Intervalos ya colocados por cada aula en esta semana.
                var ocupacion = new Dictionary<int, List<(int start, int dur)>>();

                foreach (var i in presenciales)
                {
                    int start = startPorSesion[i];
                    int dur   = duracionPorSesion[i];

                    int asignado = -1;
                    foreach (var e in CandidatosDe(sesiones[i], espacios, estudiantesPorGrupo))
                    {
                        var ocupados = ocupacion.TryGetValue(e, out var lista) ? lista : null;
                        bool libre = ocupados == null ||
                            !ocupados.Any(o => BloquesPlanner.Solapan(o.start, o.dur, start, dur, diaPorIdx));
                        if (libre) { asignado = e; break; }
                    }

                    if (asignado == -1) return null; // infactible: sin aula candidata libre

                    if (!ocupacion.TryGetValue(asignado, out var l)) { l = new(); ocupacion[asignado] = l; }
                    l.Add((start, dur));
                    resultado[(sesiones[i].Id, semana)] = espacios[asignado].Id;
                }
            }

            return resultado;
        }

        /// <summary>
        /// Índices de espacios candidatos para una sesión, con los mismos filtros que CP-SAT:
        /// HC-S05 (espacio fijo existente ⇒ único candidato), HC-S03 (laboratorio ⇒ solo labs)
        /// y HC-CAP (aforo ≥ estudiantes del grupo).
        /// </summary>
        private static IEnumerable<int> CandidatosDe(
            Sesion sesion,
            IReadOnlyList<Espacio> espacios,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo)
        {
            int estudiantes = sesion.GrupoId.HasValue && estudiantesPorGrupo != null &&
                estudiantesPorGrupo.TryGetValue(sesion.GrupoId.Value, out var n) ? n : 0;

            // HC-S05: espacio fijo → único candidato si existe en la lista (mismo criterio que CP-SAT).
            if (sesion.EspacioId.HasValue)
            {
                for (int e = 0; e < espacios.Count; e++)
                {
                    if (espacios[e].Id != sesion.EspacioId.Value) continue;
                    if (estudiantes > 0 && espacios[e].Capacidad < estudiantes) yield break; // HC-CAP
                    yield return e;
                    yield break;
                }
                // El espacio fijo no está en la lista: cae al filtrado normal (igual que CP-SAT).
            }

            bool requiereLab = RequiereLaboratorio(sesion);
            for (int e = 0; e < espacios.Count; e++)
            {
                if (requiereLab && espacios[e].Tipo != TipoEspacio.Laboratorio) continue;   // HC-S03
                if (estudiantes > 0 && espacios[e].Capacidad < estudiantes) continue;        // HC-CAP
                yield return e;
            }
        }

        /// <summary>HC-S03 (mismo criterio que CP-SAT): la sesión requiere laboratorio si y solo si su TipoFlujo lo es.</summary>
        private static bool RequiereLaboratorio(Sesion sesion) => sesion.TipoFlujo == TipoFlujo.Laboratorio;
    }
}
