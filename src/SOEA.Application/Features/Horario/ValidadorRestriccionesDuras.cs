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
        IReadOnlyDictionary<Guid, List<RequisitoEspacio>>? RequisitosPorGrupo = null,
        // G2/G7 auditoría: nombre de asignatura/grupo para que los mensajes de conflicto
        // identifiquen a un coordinador académico, no a un GUID. Ausente = los mensajes
        // degradan a "sin nombre" — nunca vuelven a mostrar el Id crudo.
        IReadOnlyDictionary<Guid, string>? NombrePorAsignatura = null,
        IReadOnlyDictionary<Guid, string>? NombrePorGrupo = null);

    /// <summary>
    /// Validador post-generación de restricciones duras (P0.3 auditoría).
    /// Recorre las asignaciones semanales FINALES (salida del motor) y cuenta violaciones reales
    /// antes de construir/publicar el <see cref="Domain.Entities.Horario"/>, en lugar de confiar en
    /// un <c>violacionesRestriccionesDuras: 0</c> hardcodeado. Detecta:
    ///   - HC-C01: un mismo grupo (cohorte) con sesiones solapadas en la misma semana (presencial o virtual).
    ///   - HC-S01: un mismo espacio físico con sesiones presenciales solapadas en la misma semana.
    ///   - HC-ALT: pareja de alternancia (A4/VERIFICA) sin tipos opuestos, o sin compartir bloque/espacio.
    /// Y, con <see cref="ContextoValidacion"/>, además:
    ///   - HC-BASE (regla 8): sesión del horario base movida de su bloque pre-asignado.
    ///   - HC-VH: sesión fuera de la ventana horaria de su asignatura.
    ///   - HC-G01: inicio fuera de la disponibilidad declarada del grupo (mismo criterio que CP-SAT/GA).
    ///   - HC-S04 (M8): asignación presencial sin espacio asignado (dirección inversa del invariante
    ///     de entidad, que sólo garantiza virtual ⇒ sin espacio).
    ///   - DATOS (M8): espacio asignado que no existe en el catálogo de espacios de la corrida —
    ///     antes se saltaba en silencio, perdiendo también HC-S03/HC-CAP/HC-S05 para esa sesión.
    ///   - HC-CAP: espacio con aforo insuficiente para los estudiantes del grupo.
    ///   - HC-S03: espacio que no cumple el tipo requerido por la sesión (A2/A3).
    ///   - HC-S05: sesión con espacio fijo (propio o del requisito de grupo) asignada a otro espacio (M7).
    ///   - HC-SEP: sesiones semanales del mismo (grupo, asignatura, tipo) sin separación mínima de días.
    /// La detección es consciente de la duración: cada asignación ocupa <c>ceil(DuracionHoras)</c>
    /// bloques contiguos a partir de su bloque de inicio (redondeo conservador: sobre-reserva,
    /// nunca sub-reserva). Como la grilla canónica se indexa por (día, hora), dos intervalos en
    /// días distintos tienen rangos de índice disjuntos.
    /// </summary>
    public static class ValidadorRestriccionesDuras
    {
        // ── Nombres, no Ids (G2/G7 auditoría) ───────────────────────────────────────
        // Único formateador para todos los mensajes de conflicto — antes cada regla
        // interpolaba el GUID directamente (~20 sitios). Degrada a texto legible cuando falta
        // el nombre (contexto ausente, p. ej. en tests que solo cubren HC-C01/HC-S01/HC-ALT),
        // nunca vuelve a mostrar el Id crudo.

        private static string NombreAsignatura(Guid asignaturaId, ContextoValidacion? ctx) =>
            ctx?.NombrePorAsignatura?.TryGetValue(asignaturaId, out var n) == true ? n : "asignatura sin nombre";

        private static string NombreGrupo(Guid grupoId, ContextoValidacion? ctx) =>
            ctx?.NombrePorGrupo?.TryGetValue(grupoId, out var n) == true ? n : "grupo sin nombre";

        private static string NombreEspacio(Guid espacioId, ContextoValidacion? ctx) =>
            ctx?.EspacioPorId?.TryGetValue(espacioId, out var e) == true ? e.Nombre : "espacio sin nombre";

        /// <summary>"Cálculo I · G1" — asignatura y, si la sesión tiene grupo, el grupo.</summary>
        private static string Describir(Sesion s, ContextoValidacion? ctx)
        {
            var asig = NombreAsignatura(s.AsignaturaId, ctx);
            return s.GrupoId.HasValue ? $"{asig} · {NombreGrupo(s.GrupoId.Value, ctx)}" : asig;
        }

        /// <summary>
        /// "Cálculo I · G1 (lunes 08:00–10:00)" — como <see cref="Describir"/>, con día y hora si
        /// hay contexto con bloques. Dos sesiones de la misma asignatura/grupo describen igual con
        /// <see cref="Describir"/> a secas (indistinguibles para el coordinador); el horario es lo
        /// que las diferencia en un mensaje de conflicto "Sesión 1 / Sesión 2".
        /// </summary>
        private static string DescribirConHorario(Intervalo item, ContextoValidacion? ctx)
        {
            var desc = Describir(item.Sesion, ctx);
            if (ctx is null || item.Inicio >= ctx.Bloques.Count) return desc;
            var bloque = ctx.Bloques[item.Inicio];
            var fin = bloque.HoraInicio.AddHours((double)item.Sesion.DuracionHoras);
            return $"{desc} ({bloque.Dia} {bloque.HoraInicio:HH\\:mm}–{fin:HH\\:mm})";
        }

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
            // INDEPENDIENTE DE LA SEMANA (ALT-05): la sesión cae en la misma franja todas las
            // semanas, y una que alterna se sigue dictando virtualmente la semana contraria.
            foreach (var grupo in items
                         .Where(i => i.Sesion.GrupoId.HasValue)
                         .GroupBy(i => i.Sesion.GrupoId!.Value))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-C01",
                    NombreGrupo(grupo.Key, contexto), contexto));

            // HC-S01 — conflicto de espacio físico (solo presencial; las virtuales no ocupan espacio).
            //
            // Se expande cada sesión a las semanas que REALMENTE ocupa el aula, en vez de usar la
            // semana de su fila. Al persistirse una sola fila por sesión, Asignacion.Semana dejó de
            // ser una clave fiable de ocupación: una sesión que no alterna tiene fila en A pero está
            // en el aula también en B, así que choca con el miembro TipoB de una pareja aunque las
            // dos filas digan semanas distintas.
            //
            // AVISO: el índice único de BD ux_asignacion_semanal_espacio_conflicto
            // (espacio_id, semana, bloque_tiempo_id) NO puede ver ese caso, porque los valores de
            // semana difieren. No se puede endurecer: una pareja comparte legítimamente aula y
            // bloque entre semanas y SQL no ve pareja_alternancia_id. La garantía real vive AQUÍ,
            // más el NoOverlap por (espacio, semana) de CP-SAT y del asignador de aulas.
            foreach (var grupo in items
                         .Where(i => i.Asignacion.Modalidad == Modalidad.Presencial && i.Asignacion.EspacioId.HasValue)
                         .SelectMany(i => ModalidadSemanal.SemanasQueOcupanEspacio(i.Sesion).Select(w => (Item: i, Semana: w)))
                         .GroupBy(x => (Espacio: x.Item.Asignacion.EspacioId!.Value, x.Semana),
                                  x => x.Item))
                conflictos.AddRange(DetectarSolapes(grupo, "HC-S01",
                    $"espacio '{NombreEspacio(grupo.Key.Espacio, contexto)}' (semana {grupo.Key.Semana})", contexto));

            // HC-ALT — alternancia atómica por espacio (A4/VERIFICA): toda sesión con pareja
            // (ParejaAlternanciaId) debe compartir bloque y aula con su pareja, tener tipos
            // opuestos, y ocupar semanas opuestas. Cada miembro tiene UNA fila.
            foreach (var parejaGrupo in items
                         .Where(i => i.Sesion.ParejaAlternanciaId.HasValue)
                         .GroupBy(i => i.Sesion.ParejaAlternanciaId!.Value))
            {
                var miembros = parejaGrupo.Select(i => i.Sesion.Id).Distinct().ToList();
                if (miembros.Count != 2)
                {
                    var descritas = string.Join(", ", parejaGrupo.Select(i => Describir(i.Sesion, contexto)).Distinct());
                    conflictos.Add($"HC-ALT: la pareja de alternancia ({descritas}) no tiene exactamente 2 sesiones ({miembros.Count}).");
                    continue;
                }

                var itemsS1 = parejaGrupo.Where(i => i.Sesion.Id == miembros[0]).ToList();
                var itemsS2 = parejaGrupo.Where(i => i.Sesion.Id == miembros[1]).ToList();
                var s1 = itemsS1[0].Sesion; var s2 = itemsS2[0].Sesion;
                var d1 = Describir(s1, contexto); var d2 = Describir(s2, contexto);

                if (s1.Alternancia == TipoAlternancia.SinAlternancia || s2.Alternancia == TipoAlternancia.SinAlternancia ||
                    s1.Alternancia == s2.Alternancia)
                    conflictos.Add($"HC-ALT: la pareja {d1} / {d2} no tiene tipos opuestos (TipoA/TipoB): " +
                                   $"{d1}={s1.Alternancia}, {d2}={s2.Alternancia}.");

                if (itemsS1[0].Inicio != itemsS2[0].Inicio)
                    conflictos.Add($"HC-ALT: la pareja {d1} / {d2} no coincide de bloque.");

                if (itemsS1[0].Asignacion.Semana == itemsS2[0].Asignacion.Semana)
                    conflictos.Add($"HC-ALT: la pareja {d1} / {d2} ocupa la misma semana " +
                                   $"({itemsS1[0].Asignacion.Semana}) en vez de alternar.");

                var esp1 = itemsS1[0].Asignacion.EspacioId;
                var esp2 = itemsS2[0].Asignacion.EspacioId;
                if (esp1 is null || esp2 is null || esp1 != esp2)
                    conflictos.Add($"HC-ALT: la pareja {d1} / {d2} no comparte el mismo espacio físico.");
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
                    conflictos.Add($"HC-BASE: la sesión fija de {Describir(s, ctx)} fue movida de su bloque del " +
                                   $"horario base (semana {a.Semana}).");
                }

                // HC-VH — ventana horaria de la asignatura. Exenta para sesiones del horario base.
                if (!esFija &&
                    ctx.VentanaPorAsignatura.TryGetValue(s.AsignaturaId, out var v) &&
                    (v.Min.HasValue || v.Max.HasValue) &&
                    !CalculadorDominioSesion.CumpleVentana(horaInicio, dur, v.Min, v.Max))
                {
                    conflictos.Add($"HC-VH: {Describir(s, ctx)} asignada a las {horaInicio:HH\\:mm} ({dur}h) " +
                                   $"fuera de la ventana [{v.Min:HH\\:mm}–{v.Max:HH\\:mm}] de su asignatura (semana {a.Semana}).");
                }

                // HC-G01 — disponibilidad del grupo por día (misma fuente que CP-SAT/GA).
                if (!esFija && s.GrupoId.HasValue &&
                    ctx.DisponibilidadPorGrupo.TryGetValue(s.GrupoId.Value, out var disp) &&
                    !disp.PermiteBloque(ctx.Bloques[inicio].Dia, horaInicio, ctx.Bloques[inicio].HoraFin))
                {
                    conflictos.Add($"HC-G01: {Describir(s, ctx)} inicia el {ctx.Bloques[inicio].Dia} a las " +
                                   $"{horaInicio:HH\\:mm}, fuera de la disponibilidad declarada del grupo (semana {a.Semana}).");
                }

                // HC-S04 (bidireccional, M8): la entidad ya garantiza virtual ⇒ sin espacio; el
                // validador no comprobaba la dirección inversa, así que una presencial sin espacio
                // asignado pasaba limpia — un horario "0 violaciones" con una sesión inagendable.
                if (a.Modalidad == Modalidad.Presencial && !a.EspacioId.HasValue)
                {
                    conflictos.Add($"HC-S04: {Describir(s, ctx)} es presencial pero no tiene espacio asignado (semana {a.Semana}).");
                }

                // Reglas de espacio: solo asignaciones presenciales con espacio.
                if (a.Modalidad != Modalidad.Presencial || !a.EspacioId.HasValue) continue;

                // M8: un espacio desconocido (fuera del catálogo de esta corrida) antes se saltaba
                // en silencio con `continue` — HC-S03/HC-CAP/HC-S05 se perdían sin dejar rastro.
                // El espacio en sí no tiene nombre que mostrar — es precisamente un id que no
                // existe en el catálogo — así que el dato útil es identificarlo como desconocido.
                if (!ctx.EspacioPorId.TryGetValue(a.EspacioId.Value, out var espacio))
                {
                    conflictos.Add($"DATOS: {Describir(s, ctx)} fue asignada a un espacio que no existe " +
                                   $"en el catálogo de espacios de esta corrida (semana {a.Semana}).");
                    continue;
                }

                // HC-S03 — tipo de espacio según TipoSesion (A2/A3), con el requisito del grupo
                // si existe (misma fuente que CP-SAT/GA: CalculadorEspaciosSesion).
                var tipoSesion = CalculadorEspaciosSesion.TipoSesionDe(s);
                RequisitoEspacio? requisito = s.GrupoId.HasValue &&
                    (ctx.RequisitosPorGrupo?.TryGetValue(s.GrupoId.Value, out var reqs) ?? false)
                    ? reqs!.FirstOrDefault(r => r.TipoSesion == tipoSesion)
                    : null;
                if (!CalculadorEspaciosSesion.CumpleTipo(espacio, tipoSesion, requisito))
                    conflictos.Add($"HC-S03: {Describir(s, ctx)} ({tipoSesion}) asignada al espacio " +
                                   $"'{espacio.Nombre}' (tipo {espacio.Tipo}), que no cumple su requisito de espacio (semana {a.Semana}).");

                // HC-CAP — aforo suficiente para los estudiantes del grupo.
                if (s.GrupoId.HasValue &&
                    ctx.EstudiantesPorGrupo.TryGetValue(s.GrupoId.Value, out var estudiantes) &&
                    estudiantes > 0 && espacio.Capacidad < estudiantes)
                {
                    conflictos.Add($"HC-CAP: {Describir(s, ctx)} en espacio '{espacio.Nombre}' (aforo {espacio.Capacidad}) " +
                                   $"para un grupo de {estudiantes} estudiantes (semana {a.Semana}).");
                }

                // HC-S05 — espacio fijo (solo si ese espacio existe en el run, mismo criterio que
                // CP-SAT). Prioriza Sesion.EspacioId (valor materializado en la sesión concreta);
                // si la sesión no trae uno, cae al requisito de espacio del GRUPO (M7: antes sólo
                // se comparaba Sesion.EspacioId — para sesiones creadas por vías que no copian el
                // requisito del grupo a la sesión, la comparación no tenía nada contra qué fallar).
                if (s.EspacioId.HasValue && ctx.EspacioPorId.ContainsKey(s.EspacioId.Value) &&
                    a.EspacioId.Value != s.EspacioId.Value)
                {
                    conflictos.Add($"HC-S05: {Describir(s, ctx)} tiene espacio fijo '{NombreEspacio(s.EspacioId.Value, ctx)}' " +
                                   $"pero fue asignada a '{NombreEspacio(a.EspacioId.Value, ctx)}' (semana {a.Semana}).");
                }
                else if (!s.EspacioId.HasValue && requisito?.EspacioId is Guid espacioFijoGrupo &&
                         ctx.EspacioPorId.ContainsKey(espacioFijoGrupo) &&
                         a.EspacioId.Value != espacioFijoGrupo)
                {
                    conflictos.Add($"HC-S05: {Describir(s, ctx)} tiene espacio fijo '{NombreEspacio(espacioFijoGrupo, ctx)}' " +
                                   $"declarado en el requisito de espacio del grupo, pero fue asignada a " +
                                   $"'{NombreEspacio(a.EspacioId.Value, ctx)}' (semana {a.Semana}).");
                }
            }

            // HC-SEP — separación mínima de días entre sesiones semanales del mismo
            // (grupo, asignatura, tipo de sesión). Eje temporal ⇒ independiente de la semana
            // (ALT-05: la sesión cae el mismo día todas las semanas). Sesiones fijas del horario
            // base quedan fuera (mismo criterio que HC-VH/HC-G01: CP-SAT no les aplica este dominio).
            foreach (var grupo in items
                         .Where(i => i.Sesion.GrupoId.HasValue && ctx.SesionesFijas?.Contains(i.Sesion.Id) != true)
                         .GroupBy(i => (GrupoId: i.Sesion.GrupoId!.Value, i.Sesion.AsignaturaId,
                                        Tipo: CalculadorEspaciosSesion.TipoSesionDe(i.Sesion))))
            {
                var lista = grupo.Where(i => i.Inicio < ctx.Bloques.Count).ToList();
                for (int x = 0; x < lista.Count; x++)
                    for (int y = x + 1; y < lista.Count; y++)
                    {
                        var diaX = ctx.Bloques[lista[x].Inicio].Dia;
                        var diaY = ctx.Bloques[lista[y].Inicio].Dia;
                        if (!ReglasSesion.SeparacionDiasOk(diaX, diaY))
                            conflictos.Add($"HC-SEP: mismo grupo/asignatura/tipo sin separación mínima de 2 días " +
                                           $"— Sesión 1: {DescribirConHorario(lista[x], ctx)}; " +
                                           $"Sesión 2: {DescribirConHorario(lista[y], ctx)}. " +
                                           "Sepárelas al menos 2 días o reduzca las sesiones semanales de este tipo.");
                    }
            }
        }

        private static IEnumerable<string> DetectarSolapes(
            IEnumerable<Intervalo> grupo, string regla, string descripcionContexto, ContextoValidacion? ctx)
        {
            var sugerencia = regla switch
            {
                "HC-C01" => "Mueva una de las dos sesiones a otro día u hora, o revise que pertenezcan al grupo correcto.",
                "HC-S01" => "Asigne un espacio distinto a una de las dos sesiones, o cámbiela a otro horario.",
                _        => "Ajuste el horario de una de las dos sesiones para que no se solapen."
            };

            var ordenados = grupo.OrderBy(i => i.Inicio).ToList();
            for (int i = 0; i < ordenados.Count; i++)
            {
                for (int j = i + 1; j < ordenados.Count; j++)
                {
                    // Como están ordenados por inicio, basta comparar contra el fin del primero.
                    if (ordenados[j].Inicio >= ordenados[i].Fin) break;
                    yield return $"{regla}: solape en {descripcionContexto} — " +
                                 $"Sesión 1: {DescribirConHorario(ordenados[i], ctx)}; " +
                                 $"Sesión 2: {DescribirConHorario(ordenados[j], ctx)}. {sugerencia}";
                }
            }
        }

        private readonly record struct Intervalo(AsignacionSemanal Asignacion, Sesion Sesion, int Inicio, int Duracion)
        { public int Fin => Inicio + Duracion; }
    }
}
