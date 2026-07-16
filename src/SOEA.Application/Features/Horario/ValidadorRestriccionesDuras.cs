using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Contexto opcional para validar el resto de restricciones duras que CP-SAT impone en
    /// Fase 2 pero que las asignaciones finales (salida del GA) podrían haber roto:
    /// HC-VH (ventana), HC-G01 (franja del grupo), HC-CAP (aforo), HC-S03 (laboratorio),
    /// HC-S05 (espacio fijo). Sin contexto, el validador solo cubre HC-C01 + HC-S01.
    /// <see cref="SesionesFijas"/>: las sesiones del horario base están exentas de HC-VH y
    /// HC-G01 (CP-SAT las fija por igualdad y no les aplica dominio — misma semántica aquí).
    /// </summary>
    public sealed record ContextoValidacion(
        IReadOnlyList<BloqueTiempo> Bloques,
        IReadOnlyDictionary<Guid, (TimeOnly? Min, TimeOnly? Max)> VentanaPorAsignatura,
        IReadOnlyDictionary<Guid, IReadOnlyList<FranjaHoraria>> FranjasPorGrupo,
        IReadOnlyDictionary<Guid, int> EstudiantesPorGrupo,
        IReadOnlyDictionary<Guid, Espacio> EspacioPorId,
        IReadOnlySet<Guid>? SesionesFijas = null);

    /// <summary>
    /// Validador post-generación de restricciones duras (P0.3 auditoría).
    /// Recorre las asignaciones semanales FINALES (salida del motor) y cuenta violaciones reales
    /// antes de construir/publicar el <see cref="Domain.Entities.Horario"/>, en lugar de confiar en
    /// un <c>violacionesRestriccionesDuras: 0</c> hardcodeado. Detecta:
    ///   - HC-C01: un mismo grupo (cohorte) con sesiones solapadas en la misma semana (presencial o virtual).
    ///   - HC-S01: un mismo espacio físico con sesiones presenciales solapadas en la misma semana.
    /// Y, con <see cref="ContextoValidacion"/>, además:
    ///   - HC-VH: sesión fuera de la ventana horaria de su asignatura.
    ///   - HC-G01: inicio fuera de la franja declarada del grupo (mismo criterio de franja que CP-SAT/GA).
    ///   - HC-CAP: espacio con aforo insuficiente para los estudiantes del grupo.
    ///   - HC-S03: sesión de laboratorio presencial en un espacio que no es laboratorio.
    ///   - HC-S05: sesión con espacio fijo asignada a otro espacio.
    /// La detección es consciente de la duración: cada asignación ocupa <c>ceil(DuracionHoras)</c>
    /// bloques contiguos a partir de su bloque de inicio (redondeo conservador: sobre-reserva,
    /// nunca sub-reserva). Como la grilla canónica se indexa por (día, hora), dos intervalos en
    /// días distintos tienen rangos de índice disjuntos.
    /// </summary>
    public static class ValidadorRestriccionesDuras
    {
        public static IReadOnlyList<string> Validar(
            IEnumerable<AsignacionSemanal> asignaciones,
            IReadOnlyDictionary<Guid, Sesion> sesionPorId,
            IReadOnlyDictionary<Guid, int> bloqueIndex,
            ContextoValidacion? contexto = null)
        {
            var conflictos = new List<string>();

            var items = new List<Intervalo>();
            foreach (var a in asignaciones)
            {
                if (!sesionPorId.TryGetValue(a.SesionId, out var s)) continue;
                if (!bloqueIndex.TryGetValue(a.BloqueTiempoId, out var inicio)) continue;
                int duracion = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                items.Add(new Intervalo(a, s, inicio, duracion));
            }

            // HC-C01 — conflicto de cohorte (presencial + virtual consumen el tiempo del grupo).
            // CR-08 (presencial-first): el grupo de estudiantes es el eje de no-solapamiento; el
            // docente sale del pipeline. Solo aplica a sesiones con grupo asignado.
            foreach (var grupo in items
                         .Where(i => i.Sesion.GrupoId.HasValue)
                         .GroupBy(i => (GrupoId: i.Sesion.GrupoId!.Value, i.Asignacion.Semana)))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-C01", $"grupo {grupo.Key.GrupoId} (semana {grupo.Key.Semana})"));

            // HC-S01 — conflicto de espacio físico (solo presencial; las virtuales no ocupan espacio).
            foreach (var grupo in items
                         .Where(i => i.Asignacion.Modalidad == Modalidad.Presencial && i.Asignacion.EspacioId.HasValue)
                         .GroupBy(i => (Espacio: i.Asignacion.EspacioId!.Value, i.Asignacion.Semana)))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-S01", $"espacio {grupo.Key.Espacio} (semana {grupo.Key.Semana})"));

            if (contexto is not null)
                ValidarConContexto(items, contexto, conflictos);

            return conflictos;
        }

        private static void ValidarConContexto(
            List<Intervalo> items, ContextoValidacion ctx, List<string> conflictos)
        {
            foreach (var item in items)
            {
                var (a, s, inicio, dur) = (item.Asignacion, item.Sesion, item.Inicio, item.Duracion);
                if (inicio >= ctx.Bloques.Count) continue;
                var horaInicio = ctx.Bloques[inicio].HoraInicio;
                bool esFija = ctx.SesionesFijas?.Contains(s.Id) == true;

                // Regla 8 (horario base): una sesión fija debe permanecer EXACTAMENTE en el bloque
                // que trae pre-asignado — CP-SAT la fija por igualdad y nadie puede moverla.
                if (esFija && s.BloqueTiempoId != Guid.Empty && a.BloqueTiempoId != s.BloqueTiempoId)
                {
                    conflictos.Add($"HC-BASE: la sesión fija {s.Id} fue movida de su bloque del " +
                                   $"horario base (semana {a.Semana}).");
                }

                // HC-VH — ventana horaria de la asignatura. Exenta para sesiones del horario base.
                if (!esFija &&
                    ctx.VentanaPorAsignatura.TryGetValue(s.AsignaturaId, out var v) &&
                    (v.Min.HasValue || v.Max.HasValue) &&
                    !CalculadorDominioSesion.CumpleVentana(horaInicio, dur, v.Min, v.Max))
                {
                    conflictos.Add($"HC-VH: sesión {s.Id} asignada a las {horaInicio:HH\\:mm} ({dur}h) " +
                                   $"fuera de la ventana [{v.Min:HH\\:mm}–{v.Max:HH\\:mm}] de su asignatura (semana {a.Semana}).");
                }

                // HC-G01 — franja del grupo (criterio de inicio, igual que CP-SAT/GA).
                if (!esFija && s.GrupoId.HasValue &&
                    ctx.FranjasPorGrupo.TryGetValue(s.GrupoId.Value, out var franjas) &&
                    franjas.Count > 0 &&
                    !CalculadorDominioSesion.CumpleFranjas(horaInicio, franjas))
                {
                    conflictos.Add($"HC-G01: sesión {s.Id} inicia a las {horaInicio:HH\\:mm}, fuera de la " +
                                   $"franja declarada del grupo {s.GrupoId} (semana {a.Semana}).");
                }

                // Reglas de espacio: solo asignaciones presenciales con espacio.
                if (a.Modalidad != Modalidad.Presencial || !a.EspacioId.HasValue) continue;
                if (!ctx.EspacioPorId.TryGetValue(a.EspacioId.Value, out var espacio)) continue;

                // HC-S03 — laboratorio presencial exige espacio tipo Laboratorio.
                if (s.TipoFlujo == TipoFlujo.Laboratorio && espacio.Tipo != TipoEspacio.Laboratorio)
                    conflictos.Add($"HC-S03: sesión de laboratorio {s.Id} asignada al espacio " +
                                   $"'{espacio.Nombre}' que no es laboratorio (semana {a.Semana}).");

                // HC-CAP — aforo suficiente para los estudiantes del grupo.
                if (s.GrupoId.HasValue &&
                    ctx.EstudiantesPorGrupo.TryGetValue(s.GrupoId.Value, out var estudiantes) &&
                    estudiantes > 0 && espacio.Capacidad < estudiantes)
                {
                    conflictos.Add($"HC-CAP: sesión {s.Id} en espacio '{espacio.Nombre}' (aforo {espacio.Capacidad}) " +
                                   $"para un grupo de {estudiantes} estudiantes (semana {a.Semana}).");
                }

                // HC-S05 — espacio fijo de la asignatura (solo si ese espacio existe en el run,
                // mismo criterio que CP-SAT).
                if (s.EspacioId.HasValue && ctx.EspacioPorId.ContainsKey(s.EspacioId.Value) &&
                    a.EspacioId.Value != s.EspacioId.Value)
                {
                    conflictos.Add($"HC-S05: sesión {s.Id} tiene espacio fijo {s.EspacioId} pero fue " +
                                   $"asignada a {a.EspacioId} (semana {a.Semana}).");
                }
            }
        }

        private static IEnumerable<string> DetectarSolapes(
            IEnumerable<Intervalo> grupo, string regla, string contexto)
        {
            var ordenados = grupo.OrderBy(i => i.Inicio).ToList();
            for (int i = 0; i < ordenados.Count; i++)
            {
                for (int j = i + 1; j < ordenados.Count; j++)
                {
                    // Como están ordenados por inicio, basta comparar contra el fin del primero.
                    if (ordenados[j].Inicio >= ordenados[i].Fin) break;
                    yield return $"{regla}: solape en {contexto} entre sesiones {ordenados[i].Sesion.Id} y {ordenados[j].Sesion.Id}.";
                }
            }
        }

        private readonly record struct Intervalo(AsignacionSemanal Asignacion, Sesion Sesion, int Inicio, int Duracion)
        { public int Fin => Inicio + Duracion; }
    }
}
