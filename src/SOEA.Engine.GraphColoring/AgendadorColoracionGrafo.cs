using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Engine.GraphColoring
{
    /// <summary>
    /// Fase 1 — Welsh-Powell duration-aware.
    /// Asigna a cada sesión un span [startIdx, startIdx+DuracionHoras) que no
    /// cruza día, respeta HC-G01/HC-VH (misma fuente que CP-SAT: <see cref="CalculadorDominioSesion"/>)
    /// y no colisiona con los spans de vecinos del grafo de conflictos.
    /// La salida sirve como warm-start para CP-SAT (no es vinculante).
    /// </summary>
    public class AgendadorColoracionGrafo : IMotorColoracionGrafo
    {
        private readonly ConstructorGrafoConflictos _constructorGrafo;
        private readonly ILogger<AgendadorColoracionGrafo> _logger;

        public AgendadorColoracionGrafo(ConstructorGrafoConflictos constructorGrafo, ILogger<AgendadorColoracionGrafo> logger)
        {
            _constructorGrafo = constructorGrafo;
            _logger = logger;
        }

        public Task<IEnumerable<Sesion>> AsignarBloquesDeTiempoAsync(
            IEnumerable<Sesion> sesiones,
            IEnumerable<BloqueTiempo> bloquesDisponibles,
            IEnumerable<Grupo>? grupos = null,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura = null,
            CancellationToken ct = default)
        {
            var s = sesiones.ToList();
            var b = bloquesDisponibles.ToList();
            var g = grupos?.ToList();
            return Task.Run<IEnumerable<Sesion>>(() => AsignarBloquesSincrono(s, b, g, ventanaPorAsignatura), ct);
        }

        private List<Sesion> AsignarBloquesSincrono(
            List<Sesion> sesiones,
            List<BloqueTiempo> bloques,
            List<Grupo>? grupos,
            IReadOnlyDictionary<Guid, (TimeOnly? min, TimeOnly? max)>? ventanaPorAsignatura)
        {
            if (!sesiones.Any() || !bloques.Any())
            {
                _logger.LogWarning("No hay sesiones o bloques disponibles para procesar.");
                return sesiones;
            }

            _logger.LogInformation(
                "Welsh-Powell duration-aware: {S} sesiones, {B} bloques.",
                sesiones.Count, bloques.Count);

            // 1. Grafo de conflictos (estructural: comparten cohorte/grupo o espacio)
            var grafo = _constructorGrafo.Construir(sesiones);

            // 2. Helpers de planeación temporal
            var rangosPorDia = BloquesPlanner.RangosPorDia(bloques);
            var diaPorIdx    = BloquesPlanner.DiaPorBloqueIdx(bloques);

            // HC-G01: mismo cálculo de franjas por grupo que CP-SAT/GA (CalculadorDominioSesion).
            var bloquesPermitidosPorGrupo = new Dictionary<Guid, HashSet<int>>();
            if (grupos != null)
            {
                foreach (var grupo in grupos)
                {
                    if (grupo.Id == Guid.Empty) continue;
                    var permitidos = CalculadorDominioSesion.BloquesPermitidos(bloques, grupo.Disponibilidad);
                    if (permitidos is not null)
                        bloquesPermitidosPorGrupo[grupo.Id] = permitidos;
                }
            }

            // 3. Pre-calcular duración por sesión y starts candidatos: cabe-en-día ∩ HC-G01 ∩ HC-VH,
            //    reordenados round-robin por día para no amontonar el warm-start en lunes temprano
            //    (con cohorte única el grafo es completo, así que el orden de intento importa).
            var duraciones = new Dictionary<Guid, int>();
            var startsValidosPorSesion = new Dictionary<Guid, int[]>();
            foreach (var s in sesiones)
            {
                int dur = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                duraciones[s.Id] = dur;

                HashSet<int>? permGrupo = null;
                if (s.GrupoId.HasValue)
                    bloquesPermitidosPorGrupo.TryGetValue(s.GrupoId.Value, out permGrupo);

                (TimeOnly? min, TimeOnly? max) ventana = default;
                ventanaPorAsignatura?.TryGetValue(s.AsignaturaId, out ventana);

                var starts = CalculadorDominioSesion.StartsPermitidos(
                    dur, bloques, rangosPorDia, diaPorIdx, permGrupo, ventana.min, ventana.max);
                startsValidosPorSesion[s.Id] = OrdenarRoundRobinPorDia(starts, diaPorIdx);
            }

            // 4. Orden Welsh-Powell: grado DESC, romper empates por duración DESC.
            var sesionesOrdenadas = sesiones
                .OrderByDescending(s => grafo[s.Id].Count)
                .ThenByDescending(s => s.DuracionHoras)
                .ToList();
            // 5. Coloreado: para cada sesión, buscar el primer start (en orden round-robin por día)
            //    cuyo span no intersecte el span de ningún vecino ya colocado.
            var bloquesOcupadosPorSesion = new Dictionary<Guid, HashSet<int>>();
            int asignadas = 0, enConflicto = 0;
            foreach (var sesion in sesionesOrdenadas)
            {
                int dur = duraciones[sesion.Id];
                var vecinos = grafo[sesion.Id];

                // Unir todos los índices ocupados por los vecinos ya coloreados.
                var bloqueado = new HashSet<int>();
                foreach (var vId in vecinos)
                {
                    if (bloquesOcupadosPorSesion.TryGetValue(vId, out var ocupados))
                        bloqueado.UnionWith(ocupados);
                }

                int? startAsignado = null;
                foreach (var start in startsValidosPorSesion[sesion.Id])
                {
                    bool libre = true;
                    for (int k = 0; k < dur; k++)
                    {
                        if (bloqueado.Contains(start + k)) { libre = false; break; }
                    }
                    if (libre) { startAsignado = start; break; }
                }

                if (startAsignado.HasValue)
                {
                    var ocupa = new HashSet<int>();
                    for (int k = 0; k < dur; k++) ocupa.Add(startAsignado.Value + k);
                    bloquesOcupadosPorSesion[sesion.Id] = ocupa;
                    sesion.AsignarBloqueTiempo(bloques[startAsignado.Value].Id);
                    asignadas++;
                }
                else
                {
                    sesion.MarcarConConflicto("No se encontró un span de bloques sin conflictos para la duración requerida.");
                    enConflicto++;
                }
            }

            _logger.LogInformation(
                "Coloreado completado: {A} asignadas, {C} en conflicto (CP-SAT las re-evaluará).",
                asignadas, enConflicto);

            return sesiones;
        }

        /// <summary>
        /// Reordena los starts (ya ascendentes) intercalando por día: primero el 1er candidato de
        /// cada día, luego el 2º de cada día, etc. Evita que first-fit amontone todas las sesiones
        /// al inicio de la semana cuando el grafo de conflictos es completo (cohorte única).
        /// </summary>
        private static int[] OrdenarRoundRobinPorDia(int[] starts, DiaDeSemana[] diaPorIdx)
        {
            if (starts.Length == 0) return starts;

            var porDia = new List<List<int>>();
            var indicePorDia = new Dictionary<DiaDeSemana, int>();
            foreach (var start in starts)
            {
                var dia = diaPorIdx[start];
                if (!indicePorDia.TryGetValue(dia, out var idx))
                {
                    idx = porDia.Count;
                    indicePorDia[dia] = idx;
                    porDia.Add(new List<int>());
                }
                porDia[idx].Add(start);
            }

            var resultado = new int[starts.Length];
            int pos = 0;
            for (int col = 0; pos < resultado.Length; col++)
                foreach (var dia in porDia)
                    if (col < dia.Count)
                        resultado[pos++] = dia[col];

            return resultado;
        }
    }
}
