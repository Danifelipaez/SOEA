using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Services;
using SOEA.Domain.ValueObjects;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Contexto opcional para validar el resto de restricciones duras que CP-SAT impone en
    /// Fase 2 pero que las asignaciones finales (salida del GA) podrían haber roto:
    /// HC-VH (ventana), HC-G01 (disponibilidad del grupo, por día), HC-CAP (aforo), HC-S03
    /// (laboratorio), HC-S05 (espacio fijo). Sin contexto, el validador solo cubre HC-C01 + HC-S01.
    /// <see cref="SesionesFijas"/>: las sesiones del horario base están exentas de HC-VH y
    /// HC-G01 (CP-SAT las fija por igualdad y no les aplica dominio — misma semántica aquí).
    /// </summary>
    public sealed record ContextoValidacion(
        IReadOnlyList<BloqueTiempo> Bloques,
        IReadOnlyDictionary<Guid, (TimeOnly? Min, TimeOnly? Max)> VentanaPorAsignatura,
        IReadOnlyDictionary<Guid, DisponibilidadSemanal> DisponibilidadPorGrupo,
        IReadOnlyDictionary<Guid, int> EstudiantesPorGrupo,
        IReadOnlyDictionary<Guid, Espacio> EspacioPorId,
        IReadOnlySet<Guid>? SesionesFijas = null,
        IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? RequisitosPorGrupo = null);

    /// <summary>
    /// Validador post-generación de restricciones duras (P0.3 auditoría).
    /// Recorre las asignaciones semanales FINALES (salida del motor) y cuenta violaciones reales
    /// antes de construir/publicar el <see cref="Domain.Entities.Horario"/>, en lugar de confiar en
    /// un <c>violacionesRestriccionesDuras: 0</c> hardcodeado. Detecta:
    ///   - HC-C01: un mismo grupo (cohorte) con sesiones solapadas en la misma semana (presencial o virtual).
    ///   - HC-S01: un mismo espacio físico con sesiones presenciales solapadas en la misma semana.
    ///   - HC-ALT: pareja de alternancia (A4/VERIFICA) sin tipos opuestos, o sin compartir bloque/espacio.
    /// Y, con <see cref="ContextoValidacion"/>, además:
    ///   - HC-VH: sesión fuera de la ventana horaria de su asignatura.
    ///   - HC-G01: inicio fuera de la disponibilidad declarada del grupo (mismo criterio que CP-SAT/GA).
    ///   - HC-CAP: espacio con aforo insuficiente para los estudiantes del grupo.
    ///   - HC-S03: espacio que no cumple el tipo requerido por la sesión (A2/A3).
    ///   - HC-S05: sesión con espacio fijo asignada a otro espacio.
    ///   - HC-SEP: sesiones semanales del mismo (grupo, asignatura, tipo) sin separación mínima de días.
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

            // HC-ALT — alternancia atómica por espacio (A4/VERIFICA): toda sesión con pareja
            // (ParejaAlternanciaId) debe compartir bloque (cada semana) y espacio (semanas
            // presenciales cruzadas) con su pareja, y ambas deben tener tipos opuestos.
            foreach (var parejaGrupo in items
                         .Where(i => i.Sesion.ParejaAlternanciaId.HasValue)
                         .GroupBy(i => i.Sesion.ParejaAlternanciaId!.Value))
            {
                var miembros = parejaGrupo.Select(i => i.Sesion.Id).Distinct().ToList();
                if (miembros.Count != 2)
                {
                    conflictos.Add($"HC-ALT: la pareja de alternancia {parejaGrupo.Key} no tiene exactamente 2 sesiones ({miembros.Count}).");
                    continue;
                }

                var itemsS1 = parejaGrupo.Where(i => i.Sesion.Id == miembros[0]).ToList();
                var itemsS2 = parejaGrupo.Where(i => i.Sesion.Id == miembros[1]).ToList();
                var s1 = itemsS1[0].Sesion; var s2 = itemsS2[0].Sesion;

                if (s1.Alternancia == TipoAlternancia.SinAlternancia || s2.Alternancia == TipoAlternancia.SinAlternancia ||
                    s1.Alternancia == s2.Alternancia)
                    conflictos.Add($"HC-ALT: la pareja {parejaGrupo.Key} no tiene tipos opuestos (TipoA/TipoB): " +
                                   $"{s1.Id}={s1.Alternancia}, {s2.Id}={s2.Alternancia}.");

                foreach (var semana in itemsS1.Select(i => i.Asignacion.Semana)
                             .Concat(itemsS2.Select(i => i.Asignacion.Semana)).Distinct())
                {
                    var b1 = itemsS1.FirstOrDefault(i => i.Asignacion.Semana == semana);
                    var b2 = itemsS2.FirstOrDefault(i => i.Asignacion.Semana == semana);
                    if (b1.Sesion is not null && b2.Sesion is not null && b1.Inicio != b2.Inicio)
                        conflictos.Add($"HC-ALT: la pareja {parejaGrupo.Key} no coincide de bloque en semana {semana} ({s1.Id} vs {s2.Id}).");
                }

                var presS1 = itemsS1.FirstOrDefault(i => i.Asignacion.Modalidad == Modalidad.Presencial);
                var presS2 = itemsS2.FirstOrDefault(i => i.Asignacion.Modalidad == Modalidad.Presencial);
                if (presS1.Sesion is not null && presS2.Sesion is not null &&
                    presS1.Asignacion.EspacioId != presS2.Asignacion.EspacioId)
                    conflictos.Add($"HC-ALT: la pareja {parejaGrupo.Key} no comparte el mismo espacio entre sus semanas presenciales ({s1.Id} vs {s2.Id}).");
            }

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

                // HC-G01 — disponibilidad del grupo por día (misma fuente que CP-SAT/GA).
                if (!esFija && s.GrupoId.HasValue &&
                    ctx.DisponibilidadPorGrupo.TryGetValue(s.GrupoId.Value, out var disp) &&
                    !disp.PermiteBloque(ctx.Bloques[inicio].Dia, horaInicio, ctx.Bloques[inicio].HoraFin))
                {
                    conflictos.Add($"HC-G01: sesión {s.Id} inicia el {ctx.Bloques[inicio].Dia} a las " +
                                   $"{horaInicio:HH\\:mm}, fuera de la disponibilidad declarada del grupo " +
                                   $"{s.GrupoId} (semana {a.Semana}).");
                }

                // Reglas de espacio: solo asignaciones presenciales con espacio.
                if (a.Modalidad != Modalidad.Presencial || !a.EspacioId.HasValue) continue;
                if (!ctx.EspacioPorId.TryGetValue(a.EspacioId.Value, out var espacio)) continue;

                // HC-S03 — tipo de espacio según TipoSesion (A2/A3), con el requisito del grupo
                // si existe (misma fuente que CP-SAT/GA: CalculadorEspaciosSesion).
                var tipoSesion = CalculadorEspaciosSesion.TipoSesionDe(s);
                RequisitoEspacio? requisito = s.GrupoId.HasValue &&
                    (ctx.RequisitosPorGrupo?.TryGetValue(s.GrupoId.Value, out var reqs) ?? false)
                    ? reqs!.FirstOrDefault(r => r.TipoSesion == tipoSesion)
                    : null;
                if (!CalculadorEspaciosSesion.CumpleTipo(espacio, tipoSesion, requisito))
                    conflictos.Add($"HC-S03: sesión {s.Id} ({tipoSesion}) asignada al espacio " +
                                   $"'{espacio.Nombre}' (tipo {espacio.Tipo}), que no cumple su requisito de espacio (semana {a.Semana}).");

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

            // HC-SEP — separación mínima de días entre sesiones semanales del mismo
            // (grupo, asignatura, tipo de sesión), por semana. Sesiones fijas del horario base
            // quedan fuera (mismo criterio que HC-VH/HC-G01: CP-SAT no les aplica este dominio).
            foreach (var grupo in items
                         .Where(i => i.Sesion.GrupoId.HasValue && ctx.SesionesFijas?.Contains(i.Sesion.Id) != true)
                         .GroupBy(i => (GrupoId: i.Sesion.GrupoId!.Value, i.Sesion.AsignaturaId,
                                        Tipo: CalculadorEspaciosSesion.TipoSesionDe(i.Sesion), i.Asignacion.Semana)))
            {
                var lista = grupo.Where(i => i.Inicio < ctx.Bloques.Count).ToList();
                for (int x = 0; x < lista.Count; x++)
                    for (int y = x + 1; y < lista.Count; y++)
                    {
                        var diaX = ctx.Bloques[lista[x].Inicio].Dia;
                        var diaY = ctx.Bloques[lista[y].Inicio].Dia;
                        if (!ReglasSesion.SeparacionDiasOk(diaX, diaY))
                            conflictos.Add($"HC-SEP: sesiones {lista[x].Sesion.Id} y {lista[y].Sesion.Id} " +
                                           $"(mismo grupo/asignatura/tipo) caen en días sin separación mínima " +
                                           $"({diaX} / {diaY}, semana {grupo.Key.Semana}).");
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
