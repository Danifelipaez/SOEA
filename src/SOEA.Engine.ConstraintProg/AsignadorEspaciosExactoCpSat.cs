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
    /// mismo patrón que el bloque de espacios de <see cref="MotorConstraintProgramming"/>,
    /// adaptado a intervalos de inicio FIJO en vez de variable.
    ///
    /// Una sesión tiene UN aula para todo el semestre. Lo que decide la semana es en qué NoOverlap
    /// entra ese aula (<see cref="ModalidadSemanal.SemanasQueOcupanEspacio"/>): lo que no alterna
    /// entra en las dos semanas, un TipoA solo en A y un TipoB solo en B. Por eso una pareja de
    /// alternancia puede compartir aula y bloque, y por eso emparejar libera capacidad.
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

        public Dictionary<Guid, Guid>? Asignar(
            IReadOnlyList<Sesion> sesiones,
            int[] startPorSesion,
            int[] duracionPorSesion,
            IReadOnlyList<Espacio> espacios,
            DiaDeSemana[] diaPorIdx,
            IReadOnlyDictionary<Guid, int>? estudiantesPorGrupo = null,
            IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? requisitosPorGrupo = null)
        {
            var ocupanAula = new List<int>();
            for (int i = 0; i < sesiones.Count; i++)
                if (ModalidadSemanal.SemanasQueOcupanEspacio(sesiones[i]).Count > 0)
                    ocupanAula.Add(i);

            if (ocupanAula.Count == 0) return new Dictionary<Guid, Guid>();
            if (espacios.Count == 0) return null; // hay presenciales pero ningún espacio: infactible

            var model = new CpModel();
            var spaceVars = new Dictionary<int, IntVar>();
            var optIntervalsPorEspacioSemana = new Dictionary<(int espacio, SemanaAcademica semana), List<IntervalVar>>();
            for (int e = 0; e < espacios.Count; e++)
                foreach (var semana in Semanas)
                    optIntervalsPorEspacioSemana[(e, semana)] = new List<IntervalVar>();

            foreach (var idx in ocupanAula)
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

                var spaceVar = model.NewIntVar(0, espacios.Count - 1, $"space_{idx}");
                spaceVars[idx] = spaceVar;

                var startConst = model.NewConstant(startPorSesion[idx]);
                var semanasOcupadas = ModalidadSemanal.SemanasQueOcupanEspacio(sesion);
                var literales = new List<ILiteral>();
                foreach (var e in candidatos)
                {
                    var lit = model.NewBoolVar($"sel_{idx}_{e}");
                    literales.Add(lit);
                    model.Add(spaceVar == e).OnlyEnforceIf(lit);

                    var interval = model.NewOptionalFixedSizeIntervalVar(
                        startConst, duracionPorSesion[idx], lit, $"int_{idx}_{e}");
                    foreach (var semana in semanasOcupadas)
                        optIntervalsPorEspacioSemana[(e, semana)].Add(interval);
                }
                model.AddExactlyOne(literales);
            }

            // HC-ALT: una pareja de alternancia comparte el MISMO aula. Que no colisionen lo
            // garantiza el reparto por semana de arriba (una entra solo en A, la otra solo en B).
            foreach (var pareja in sesiones
                         .Select((s, i) => (s, i))
                         .Where(x => x.s.ParejaAlternanciaId.HasValue)
                         .GroupBy(x => x.s.ParejaAlternanciaId!.Value)
                         .Where(g => g.Count() == 2))
            {
                var miembros = pareja.ToList();
                if (spaceVars.TryGetValue(miembros[0].i, out var sv1) &&
                    spaceVars.TryGetValue(miembros[1].i, out var sv2))
                    model.Add(sv1 == sv2);
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

            var resultado = new Dictionary<Guid, Guid>();
            foreach (var idx in ocupanAula)
                resultado[sesiones[idx].Id] = espacios[(int)solver.Value(spaceVars[idx])].Id;
            return resultado;
        }
    }
}
