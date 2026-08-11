using System;
using System.Collections.Generic;
using System.Linq;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Engine.Genetic
{
    /// <summary>
    /// Asigna aulas a las sesiones presenciales DESPUÉS de que el GA fijó los inicios.
    /// Como los inicios son fijos, el problema se reduce a un coloreo de intervalos por
    /// (espacio, semana): greedy por inicio = óptimo para intervalos. Garantiza HC-S01
    /// (no dos sesiones presenciales solapadas en el mismo aula/semana), HC-S03/HC-S05
    /// (tipo/espacio fijo, vía <see cref="CalculadorEspaciosSesion"/> — A2) y HC-CAP
    /// (aforo: Capacidad ≥ estudiantes inscritos del grupo) — los mismos filtros de
    /// candidatos que CP-SAT aplica en Fase 2.
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
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo = null,
            IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo = null)
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
                    foreach (var e in CandidatosDe(sesiones[i], espacios, estudiantesPorGrupo, requisitosPorGrupo))
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
        /// Índices de espacios candidatos para una sesión: HC-S05 + HC-S03 vía
        /// <see cref="CalculadorEspaciosSesion.Candidatos"/> (misma fuente que CP-SAT — A2),
        /// más HC-CAP (aforo ≥ estudiantes del grupo).
        /// </summary>
        private static IEnumerable<int> CandidatosDe(
            Sesion sesion,
            IReadOnlyList<Espacio> espacios,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo,
            IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo)
        {
            int estudiantes = sesion.GrupoId.HasValue && estudiantesPorGrupo != null &&
                estudiantesPorGrupo.TryGetValue(sesion.GrupoId.Value, out var n) ? n : 0;

            RequisitoEspacio? requisito = sesion.GrupoId.HasValue && requisitosPorGrupo != null &&
                requisitosPorGrupo.TryGetValue(sesion.GrupoId.Value, out var reqs)
                ? reqs.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(sesion))
                : null;

            foreach (var e in CalculadorEspaciosSesion.Candidatos(sesion, espacios, requisito))
                if (estudiantes == 0 || espacios[e].Capacidad >= estudiantes) // HC-CAP
                    yield return e;
        }
    }
}
