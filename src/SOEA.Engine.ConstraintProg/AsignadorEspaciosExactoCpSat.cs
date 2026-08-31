using System;
using System.Collections.Generic;
using System.Linq;
using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Engine.ConstraintProg
{
    /// <summary>
    /// Implementación CP-SAT de <see cref="IAsignadorEspaciosExacto"/>. Con los inicios ya
    /// fijados por el GA, el sub-modelo solo necesita variables 'space' + NoOverlap por espacio —
    /// mismo patrón que el bloque de espacios de <see cref="MotorConstraintProgramming"/>
    /// (líneas ~400-497), adaptado a intervalos de inicio FIJO en vez de variable. También cierra
    /// un gap que el greedy anterior nunca cubrió: HC-ALT exige que una pareja de alternancia
    /// comparta el MISMO espacio físico entre sus semanas presenciales — el greedy procesaba
    /// semana A y B de forma completamente independiente y no lo garantizaba.
    /// </summary>
    public class AsignadorEspaciosExactoCpSat : IAsignadorEspaciosExacto
    {
        private readonly ILogger<AsignadorEspaciosExactoCpSat> _logger;
        private static readonly SemanaAcademica[] Semanas = { SemanaAcademica.A, SemanaAcademica.B };

        // ponytail: inicios ya fijos, solo queda elegir espacio — el sub-modelo es diminuto y
        // debería resolverse en milisegundos. 10s es un techo de seguridad, no un presupuesto
        // esperado; si algún día hace falta afinarlo, promover a CpSatOptions (igual que Fase 2).
        private const int TimeoutSegundos = 10;

        public AsignadorEspaciosExactoCpSat(ILogger<AsignadorEspaciosExactoCpSat> logger)
        {
            _logger = logger;
        }

        public Dictionary<(Guid sesionId, SemanaAcademica semana), Guid>? Asignar(
            IReadOnlyList<Sesion> sesiones,
            int[] startAPorSesion,
            int[] startBPorSesion,
            int[] duracionPorSesion,
            IReadOnlyList<Espacio> espacios,
            DiaDeSemana[] diaPorIdx,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo = null,
            IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo = null)
        {
            var presenciales = new List<(int idx, SemanaAcademica semana, int start, int dur)>();
            for (int i = 0; i < sesiones.Count; i++)
                foreach (var semana in Semanas)
                {
                    if (ModalidadSemanal.Derivar(sesiones[i], semana) != Modalidad.Presencial) continue;
                    int start = semana == SemanaAcademica.A ? startAPorSesion[i] : startBPorSesion[i];
                    presenciales.Add((i, semana, start, duracionPorSesion[i]));
                }

            if (presenciales.Count == 0) return new Dictionary<(Guid, SemanaAcademica), Guid>();
            if (espacios.Count == 0) return null; // hay presenciales pero ningún espacio: infactible

            var model = new CpModel();
            var spaceVars = new Dictionary<(int idx, SemanaAcademica semana), IntVar>();
            // Un slot de un espacio puede reusarse en semanas distintas (mismo criterio que
            // MotorConstraintProgramming:460-464): NoOverlap independiente por (espacio, semana),
            // NUNCA pooleado entre A y B.
            var optIntervalsPorEspacioSemana = new Dictionary<(int espacio, SemanaAcademica semana), List<IntervalVar>>();
            for (int e = 0; e < espacios.Count; e++)
                foreach (var semana in Semanas)
                    optIntervalsPorEspacioSemana[(e, semana)] = new List<IntervalVar>();

            foreach (var (idx, semana, start, dur) in presenciales)
            {
                var sesion = sesiones[idx];
                int estudiantes = sesion.GrupoId.HasValue && estudiantesPorGrupo != null &&
                    estudiantesPorGrupo.TryGetValue(sesion.GrupoId.Value, out var nEst) ? nEst : 0;
                RequisitoEspacio? requisito = sesion.GrupoId.HasValue && requisitosPorGrupo != null &&
                    requisitosPorGrupo.TryGetValue(sesion.GrupoId.Value, out var reqs)
                    ? reqs.FirstOrDefault(r => r.TipoSesion == CalculadorEspaciosSesion.TipoSesionDe(sesion))
                    : null;

                var candidatos = CalculadorEspaciosSesion.Candidatos(sesion, espacios, requisito)
                    .Where(e => estudiantes == 0 || espacios[e].Capacidad >= estudiantes)
                    .ToList();
                if (candidatos.Count == 0) return null; // sin candidato: infactible (mismo criterio que el greedy)

                var spaceVar = model.NewIntVar(0, espacios.Count - 1, $"space_{idx}_{semana}");
                spaceVars[(idx, semana)] = spaceVar;

                var startConst = model.NewConstant(start);
                var literales = new List<ILiteral>();
                foreach (var e in candidatos)
                {
                    var lit = model.NewBoolVar($"sel_{idx}_{semana}_{e}");
                    literales.Add(lit);
                    model.Add(spaceVar == e).OnlyEnforceIf(lit);

                    var interval = model.NewOptionalFixedSizeIntervalVar(startConst, dur, lit, $"int_{idx}_{semana}_{e}");
                    optIntervalsPorEspacioSemana[(e, semana)].Add(interval);
                }
                model.AddExactlyOne(literales);
            }

            // HC-ALT: una pareja de alternancia comparte el MISMO espacio físico entre sus dos
            // semanas presenciales (mismo criterio que MotorConstraintProgramming:241-259).
            foreach (var pareja in sesiones
                         .Select((s, i) => (s, i))
                         .Where(x => x.s.ParejaAlternanciaId.HasValue)
                         .GroupBy(x => x.s.ParejaAlternanciaId!.Value)
                         .Where(g => g.Count() == 2))
            {
                var miembros = pareja.ToList();
                var i1 = miembros[0].i;
                var i2 = miembros[1].i;
                if (spaceVars.TryGetValue((i1, SemanaAcademica.A), out var sv1A) &&
                    spaceVars.TryGetValue((i2, SemanaAcademica.B), out var sv2B))
                    model.Add(sv1A == sv2B);
                else if (spaceVars.TryGetValue((i1, SemanaAcademica.B), out var sv1B) &&
                         spaceVars.TryGetValue((i2, SemanaAcademica.A), out var sv2A))
                    model.Add(sv1B == sv2A);
            }

            foreach (var lista in optIntervalsPorEspacioSemana.Values)
                if (lista.Count > 1)
                    model.AddNoOverlap(lista);

            var solver = new CpSolver();
            solver.StringParameters = $"max_time_in_seconds:{TimeoutSegundos}";
            var status = solver.Solve(model);

            if (status != CpSolverStatus.Feasible && status != CpSolverStatus.Optimal)
            {
                _logger.LogWarning(
                    "Fase 3: asignación exacta de espacios sin solución (status {Status}) para el mejor cromosoma.",
                    status);
                return null;
            }

            var resultado = new Dictionary<(Guid, SemanaAcademica), Guid>();
            foreach (var (idx, semana, _, _) in presenciales)
            {
                var espacioIdx = (int)solver.Value(spaceVars[(idx, semana)]);
                resultado[(sesiones[idx].Id, semana)] = espacios[espacioIdx].Id;
            }
            return resultado;
        }
    }
}
